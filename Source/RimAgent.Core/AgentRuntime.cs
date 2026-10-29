using System.Text.Json.Nodes;

namespace RimAgent.Core;

public sealed partial class AgentRuntime(IAgentGame game, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly int threadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<string, Job> jobs = [];
    private readonly Dictionary<string, (string LeaseId, string Name, JsonObject Arguments, ToolReply Reply)> receipts = [];
    private readonly Queue<RuntimeEvent> events = [];
    private readonly ControllerAccess access = new(clock ?? TimeProvider.System);
    private readonly RelayInbox inbox = new(clock ?? TimeProvider.System);
    private string setupSession = Guid.NewGuid().ToString("N");
    private bool accessInitialized;
    private string? stepCaller;
    private void InitializeAccess()
    {
        if (accessInitialized) return;
        accessInitialized = true;
        access.Released = lease => {
            foreach (var job in jobs.Values.Where(j => j.LeaseId == lease.Token)) Cancel(job);
            ClearHistory(lease.Token);
            if (lease.Mode == "operation") (game as IAgentOperations)?.ReleaseOperations();
            else inbox.Cancel();
            Emit("controller_released_" + lease.Mode);
        };
    }
    private long cursor;
    public string? SessionId { get; private set; }
    public bool IsControlled { get { CheckThread(); access.Expire(); return access.Leases.Any(); } }
    public bool IsNarrativeControlled { get { CheckThread(); access.Expire(); return access.Has("narrative"); } }
    public Action<string>? ControlAcquired { get; set; }
    public RelayRequest EnqueueRequest(string kind, int? mapId, object context, Action<JsonObject>? validate = null)
    {
        CheckThread();
        if (!IsNarrativeControlled) throw new BridgeException("no_narrator", "No external narrative controller.");
        var request = inbox.Add(kind, mapId, context, validate); Emit("request_" + kind, request.Id); return request;
    }

    public void Begin()
    {
        CheckThread(); End(); SessionId = Guid.NewGuid().ToString("N"); Emit("session_started");
    }
    public void End()
    {
        CheckThread(); InitializeAccess(); access.Clear(); inbox.Clear();
        (game as IAgentOperations)?.ReleaseOperations();
        foreach (var job in jobs.Values) Retire(job);
        jobs.Clear(); receipts.Clear(); checkpoints.Clear(); events.Clear(); cursor = 0;
        SessionId = null; setupSession = Guid.NewGuid().ToString("N");
    }
    public void Pump()
    {
        CheckThread(); InitializeAccess(); access.Expire(); inbox.Expire();
        if (stepCaller != null && !access.Active(stepCaller)) { (game as IAgentOperations)?.ReleaseOperations(); stepCaller = null; }
        (game as IAgentOperations)?.UpdateOperations();
        foreach (var job in jobs.Values)
        {
            if (job.Status == "running" && !access.Active(job.CallerId)) Cancel(job);
            if (job.Status != "running" || !job.Pending.IsCompleted) continue;
            try { job.Result = job.GetResult(); job.Status = "completed"; }
            catch (OperationCanceledException) { job.Status = "cancelled"; }
            catch (Exception ex) { job.Status = "failed"; job.Error = ex.Message; }
            job.Cancellation.Dispose(); job.Disposed = true;
            Emit("task_" + job.Status, job.Id);
        }
    }
    public ToolReply Execute(string name, JsonObject args)
    {
        CheckThread();
        try
        {
            Pump();
            var client = args["_clientId"]?.GetValue<string>() ?? "local";
            var effectiveSession = SessionId ?? setupSession;
            if (name == "rimagent_session_get")
                return ToolReply.Ok(new { sessionId = effectiveSession, ready = SessionId != null,
                    controller = access.Leases.FirstOrDefault(l => l.Mode == "operation")?.Owner,
                    controllers = access.Leases.Select(l => new { mode = l.Mode, owner = l.Owner, expiresAt = l.Expires }).ToArray(), game = game.GetState() });
            if (SessionId == null && !ControllerCatalog.MenuTool(name)) throw new BridgeException("no_game", "This operation needs a loaded game; configuration is available at the main menu.");
            if (args.Text("sessionId", 64) != effectiveSession) throw new BridgeException("stale_session", "The game changed. Read session_get again.");
            if (name == "rimagent_controller_acquire")
            {
                var (lease, fresh) = access.Acquire(client, args);
                if (fresh)
                {
                    try { ControlAcquired?.Invoke(lease.Mode); }
                    catch { access.Release(lease); throw; }
                    Emit("controller_acquired_" + lease.Mode);
                }
                return ToolReply.Ok(new { sessionId = effectiveSession, owner = lease.Owner, mode = lease.Mode, leaseToken = lease.Token, expiresAt = lease.Expires, historyEpoch = lease.HistoryEpoch });
            }
            var caller = access.Authorize(client, name, args);
            if (name == "rimagent_controller_checkpoint") return Checkpoint(caller, args);
            var operation = AutonomyCatalog.Find(name);
            if (name == "rimagent_worker_get") return ToolReply.Ok(ControllerAccess.View(access.GetWorker(caller, args)));
            if (name is "rimagent_requests_read" or "rimagent_requests_wait") { ControllerAccess.Root(caller); return ToolReply.Ok(inbox.Read()); }
            if (operation?.Kind == OperationKind.Read) return ToolReply.Ok(Operations().Read(name, args));
            if (name == "rimagent_agents_list") return ToolReply.Ok(game.ListAgents());
            if (name == "rimagent_colony_get") return ToolReply.Ok(game.CaptureColony(args.Number("mapId", 0, int.MaxValue)));
            if (name == "rimagent_events_read")
            {
                var after = args.Number("afterCursor", 0, int.MaxValue, 0);
                return ToolReply.Ok(new { events = events.Where(e => e.Cursor > after).Take(100).ToArray(), latestCursor = cursor,
                    gap = events.Count > 0 && after < events.Peek().Cursor - 1 });
            }
            if (name == "rimagent_controller_renew")
            {
                ControllerAccess.Root(caller);
                caller.Parent.Expires = clock.GetUtcNow().AddSeconds(args.Number("ttlSeconds", 30, 900, 300));
                return ToolReply.Ok(new { expiresAt = caller.Parent.Expires, history = History(caller.Parent) });
            }
            if (name == "rimagent_controller_release") { ControllerAccess.Root(caller); access.Release(caller.Parent); return ToolReply.Ok(new { released = true }); }
            if (name == "rimagent_task_get") return ToolReply.Ok(JobView(GetOwnedJob(args, caller)));
            if (name == "rimagent_task_cancel") { var job = GetOwnedJob(args, caller); Cancel(job); return ToolReply.Ok(JobView(job)); }
            if (caller.Worker == null && args.Number("historyEpoch", 0, int.MaxValue, 0) != caller.Parent.HistoryEpoch)
                throw new BridgeException("history_epoch", "History was acknowledged. Use the historyEpoch returned by controller_checkpoint for NEW mutations; never replay old actions in a new epoch.");
            if (name == "rimagent_worker_revoke")
            {
                ControllerAccess.Root(caller); var worker = access.GetWorker(caller, args); worker.Status = "revoked";
                foreach (var job in jobs.Values.Where(j => j.CallerId == worker.Id)) Cancel(job);
                return ToolReply.Ok(ControllerAccess.View(worker));
            }
            var requestId = caller.Identity + ":" + args.Text("requestId", 128);
            if (receipts.TryGetValue(requestId, out var prior))
            {
                if (prior.Name != name || !JsonNode.DeepEquals(prior.Arguments, args)) throw new BridgeException("request_conflict", "requestId was used with different arguments.");
                return prior.Reply;
            }
            if (receipts.Values.Count(r => r.LeaseId == caller.Parent.Token) >= 1024)
                throw new BridgeException("receipt_capacity", "Controller history is full. Read completed results, finish/cancel active tasks and workers, then call controller_checkpoint to continue in the same game.");
            ToolReply reply;
            switch (name)
            {
                case "rimagent_worker_create": reply = ToolReply.Ok(access.CreateWorker(caller, args)); break;
                case "rimagent_worker_complete":
                    if (caller.Worker == null) throw new BridgeException("worker_required", "Only the delegated child can report its result.");
                    if (caller.Worker.Status != "running") throw new BridgeException("worker_closed", "Worker already completed; retry its original requestId to retrieve the receipt.");
                    caller.Worker.Result = Result(args); caller.Worker.Status = "completed";
                    foreach (var job in jobs.Values.Where(j => j.CallerId == caller.Identity)) Cancel(job);
                    reply = ToolReply.Ok(ControllerAccess.View(caller.Worker)); break;
                case "rimagent_request_complete":
                    ControllerAccess.Root(caller);
                    var message = inbox.Get(args.Text("messageId", 64));
                    if (message.Result.IsCompleted) throw new BridgeException("request_closed", "Relay message is already completed or cancelled.");
                    var result = Result(args);
                    if (game is IAgentRelay relay) relay.ValidateRelayResult(message.Kind, message.MapId, message.Context!.AsObject(), result);
                    else if (message.Kind != "story_cycle") result.Text("text", 16000);
                    if (!message.TryComplete(result)) throw new BridgeException("request_closed", "Relay message was closed.");
                    reply = ToolReply.Ok(new { accepted = true, messageId = message.Id }); break;
                case "rimagent_task_submit": reply = Submit(args, caller); break;
                case "rimagent_set_paused": reply = ToolReply.Ok(game.SetPaused(args.Flag("paused"))); break;
                case "rimagent_set_drafted": reply = ToolReply.Ok(game.SetDrafted(args.Number("mapId", 0, int.MaxValue), args.Text("pawnId", 128), args.Flag("drafted"))); break;
                default:
                    if (operation == null) throw new BridgeException("unknown_tool", name);
                    if (name == "rimagent_config_update") ControllerAccess.Root(caller);
                    reply = operation.Kind == OperationKind.Job ? SubmitOperation(name, args, caller) : ToolReply.Ok(Operations().Act(name, args));
                    if (name == "rimagent_step") stepCaller = caller.Identity;
                    break;
            }
            receipts.Add(requestId, (caller.Parent.Token, name, (JsonObject)args.DeepClone(), reply)); Emit("action_applied"); return reply;
        }
        catch (BridgeException ex) { return ToolReply.Fail(ex.Code, ex.Message); }
        catch (Exception ex) { return ToolReply.Fail("execution_failed", ex.Message); }
    }
    private static JsonObject Result(JsonObject args)
    {
        if (args["result"] is not JsonObject result || result.ToJsonString().Length > 32000) throw new BridgeException("invalid_result", "result must be an object of at most 32000 characters.");
        return (JsonObject)result.DeepClone();
    }
    private Job GetOwnedJob(JsonObject args, ControllerAccess.Caller caller)
    {
        var job = GetJob(args);
        if (job.LeaseId != caller.Parent.Token || caller.Worker != null && job.CallerId != caller.Identity) throw new BridgeException("task_forbidden", "Task belongs to another controller or worker.");
        return job;
    }
    private ToolReply Submit(JsonObject args, ControllerAccess.Caller caller)
    {
        CheckTaskCapacity(caller.Parent);
        var agentId = args.Text("agentId", 64);
        if (!game.ListAgents().Any(a => a.Id == agentId && a.Available))
            throw new BridgeException("agent_unavailable", "Select an available agent from agents_list.");
        var instruction = args.Text("instruction", 8000);
        var mapId = args.Number("mapId", 0, int.MaxValue);
        var maxTokens = args.Number("maxTokens", 128, 4096, 1200);
        var snapshot = game.CaptureColony(mapId);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        Task<AnalysisResult> pending;
        try { pending = game.Analyze(agentId, instruction, snapshot, maxTokens, cts.Token); }
        catch { cts.Dispose(); throw; }
        var job = new Job(Guid.NewGuid().ToString("N"), agentId, mapId, pending, () => pending.GetAwaiter().GetResult(), cts, caller.Parent.Token, caller.Identity);
        jobs.Add(job.Id, job);
        return ToolReply.Ok(JobView(job));
    }
    private IAgentOperations Operations() => game as IAgentOperations ?? throw new BridgeException("unsupported", "Game adapter does not implement this operation.");
    private ToolReply SubmitOperation(string tool, JsonObject args, ControllerAccess.Caller caller)
    {
        var mapId = args.Number("mapId", 0, int.MaxValue, 0);
        CheckTaskCapacity(caller.Parent);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        Task<object> pending;
        try { pending = Operations().BeginOperation(tool, args, cts.Token); }
        catch { cts.Dispose(); throw; }
        var job = new Job(Guid.NewGuid().ToString("N"), tool, mapId, pending, () => pending.GetAwaiter().GetResult(), cts, caller.Parent.Token, caller.Identity);
        jobs.Add(job.Id, job);
        return ToolReply.Ok(JobView(job));
    }
    private Job GetJob(JsonObject args) => jobs.TryGetValue(args.Text("taskId", 64), out var job)
        ? job : throw new BridgeException("task_not_found", "Unknown task in this game session.");
    private static object JobView(Job job) => new { taskId = job.Id, agentId = job.AgentId, mapId = job.MapId, status = job.Status, result = job.Result, error = job.Error };
    private void Cancel(Job job)
    {
        if (job.Status != "running") return;
        job.Status = "cancelled"; Retire(job); Emit("task_cancelled", job.Id);
    }
    private static void Retire(Job job)
    {
        if (job.Disposed) return;
        job.Disposed = true; job.Cancellation.Cancel();
        _ = ObserveAndDispose(job);
    }
    private static async Task ObserveAndDispose(Job job)
    {
        try { await job.Pending.ConfigureAwait(false); } catch { /* Result is abandoned by its owner. */ }
        finally { job.Cancellation.Dispose(); }
    }
    private void Emit(string kind, string? taskId = null)
    {
        events.Enqueue(new(++cursor, kind, taskId, clock.GetUtcNow()));
        while (events.Count > 512) events.Dequeue();
    }
    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != threadId) throw new InvalidOperationException("AgentRuntime is main-thread owned.");
    }
    private sealed class Job(string id, string agentId, int mapId, Task pending, Func<object> getResult, CancellationTokenSource cancellation, string leaseId, string callerId)
    {
        public string Id = id, AgentId = agentId, Status = "running", LeaseId = leaseId, CallerId = callerId;
        public int MapId = mapId;
        public Task Pending = pending;
        public Func<object> GetResult = getResult;
        public CancellationTokenSource Cancellation = cancellation;
        public object? Result;
        public string? Error;
        public bool Disposed;
    }
}
