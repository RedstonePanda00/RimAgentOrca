using System.Text.Json.Nodes;

namespace RimAgent.Core;

internal sealed class ControllerAccess(TimeProvider clock)
{
    internal sealed class Lease(string client, string mode, string owner, string request, DateTimeOffset expiry)
    {
        public readonly string Client = client, Mode = mode, Owner = owner, Request = request, Token = Guid.NewGuid().ToString("N");
        public DateTimeOffset Expires = expiry;
        public int HistoryEpoch;
    }
    internal sealed class Worker(Lease parent, string objective, HashSet<string> tools, int? mapId, DateTimeOffset expiry)
    {
        public readonly string Id = Guid.NewGuid().ToString("N"), Token = Guid.NewGuid().ToString("N"), Objective = objective;
        public readonly Lease Parent = parent;
        public readonly HashSet<string> Tools = tools;
        public readonly int? MapId = mapId;
        public readonly DateTimeOffset Expires = expiry;
        public string? Client;
        public string Status = "running";
        public JsonObject? Result;
    }
    internal sealed record Caller(Lease Parent, Worker? Worker)
    {
        public string Identity => Worker?.Id ?? Parent.Token;
        public string Mode => Parent.Mode;
    }
    private readonly Dictionary<string, Lease> leases = [];
    private readonly Dictionary<string, string> modes = [];
    private readonly Dictionary<string, Worker> workers = [];
    private readonly HashSet<string> childClients = [];
    private readonly Dictionary<string, int> nextHistoryEpoch = [];
    public IEnumerable<Lease> Leases => leases.Values;
    public Action<Lease>? Released;
    public bool Has(string mode) => leases.ContainsKey(mode);
    public bool Active(string identity) => leases.Values.Any(l => l.Token == identity) || workers.Values.Any(w => w.Id == identity && w.Status == "running");
    public void Expire()
    {
        foreach (var lease in leases.Values.Where(l => l.Expires <= clock.GetUtcNow()).ToArray()) Release(lease);
        foreach (var worker in workers.Values.Where(w => w.Status == "running" && w.Expires <= clock.GetUtcNow())) worker.Status = "expired";
    }
    public void Clear()
    {
        foreach (var lease in leases.Values.ToArray()) Release(lease);
        workers.Clear(); modes.Clear(); childClients.Clear(); nextHistoryEpoch.Clear();
    }
    public (Lease Lease, bool Fresh) Acquire(string client, JsonObject a)
    {
        var mode = a.Text("mode", 16);
        if (mode is not ("narrative" or "operation")) throw new BridgeException("invalid_mode", "mode must be narrative or operation.");
        Bind(client, mode);
        if (childClients.Contains(client)) throw new BridgeException("worker_only", "A delegated worker connection cannot acquire a controller.");
        var request = a.Text("requestId", 128); var owner = a.Text("owner", 128);
        var ttl = a.Number("ttlSeconds", 30, 900, 300);
        if (leases.TryGetValue(mode, out var current))
        {
            if (current.Client == client && current.Request == request && current.Owner == owner) return (current, false);
            throw new BridgeException("controller_busy", "This mode already has a controller.");
        }
        var lease = new Lease(client, mode, owner, request, clock.GetUtcNow().AddSeconds(ttl))
            { HistoryEpoch = nextHistoryEpoch.GetValueOrDefault(mode) };
        leases.Add(mode, lease);
        return (lease, true);
    }
    private void Bind(string client, string mode)
    {
        if (modes.TryGetValue(client, out var prior) && prior != mode) throw new BridgeException("mode_conflict", "One connection cannot act as both narrator and player. Open a separate conversation/connection.");
        if (!modes.ContainsKey(client) && modes.Count >= 256) throw new BridgeException("client_capacity", "Too many clients in this session.");
        modes[client] = mode;
    }
    public Caller Authorize(string client, string tool, JsonObject a)
    {
        if (a["grantToken"] != null)
        {
            var token = a.Text("grantToken", 64);
            var w = workers.Values.FirstOrDefault(w => w.Token == token && (w.Status == "running" || w.Status == "completed" && tool is "rimagent_worker_get" or "rimagent_worker_complete"))
                ?? throw new BridgeException("grant_invalid", "Worker grant is expired, revoked or completed.");
            if (!leases.ContainsKey(w.Parent.Mode) || w.Parent.Expires <= clock.GetUtcNow()) throw new BridgeException("grant_invalid", "Parent controller is no longer active.");
            if (leases.Values.Any(l => l.Client == client)) throw new BridgeException("controller_connection", "Worker grants need a separate child connection.");
            Bind(client, w.Parent.Mode);
            if (w.Client != null && w.Client != client) throw new BridgeException("grant_bound", "Grant is already bound to a different child connection.");
            if (workers.Values.Any(other => other != w && other.Client == client)) throw new BridgeException("grant_bound", "Child connection is already assigned to another task.");
            if (tool is not ("rimagent_worker_get" or "rimagent_worker_complete" or "rimagent_task_get" or "rimagent_task_cancel") && !w.Tools.Contains(tool))
                throw new BridgeException("tool_forbidden", "Tool is outside this task's granted capabilities.");
            if (w.MapId.HasValue && a["mapId"] != null && a.Number("mapId", 0, int.MaxValue) != w.MapId) throw new BridgeException("map_forbidden", "Map is outside this task's grant.");
            w.Client = client;
            childClients.Add(client);
            return new(w.Parent, w);
        }
        var lease = leases.Values.FirstOrDefault(l => l.Client == client && (a["leaseToken"] == null || l.Token == a.Text("leaseToken", 64)))
            ?? throw new BridgeException("controller_required", "Acquire a controller or supply a worker grant on this connection.");
        if (!ControllerCatalog.Allows(lease.Mode, tool)) throw new BridgeException("mode_forbidden", "Tool is unavailable in " + lease.Mode + " mode.");
        return new(lease, null);
    }
    public void Release(Lease lease)
    {
        leases.Remove(lease.Mode);
        nextHistoryEpoch[lease.Mode] = checked(lease.HistoryEpoch + 1);
        foreach (var w in workers.Values.Where(w => w.Parent == lease && w.Status == "running")) w.Status = "revoked";
        Released?.Invoke(lease);
        ClearWorkers(lease);
    }
    public int WorkerCount(Lease lease) => workers.Values.Count(w => w.Parent == lease);
    public bool HasRunningWorkers(Lease lease) => workers.Values.Any(w => w.Parent == lease && w.Status == "running");
    public void ClearWorkers(Lease lease)
    {
        foreach (var id in workers.Values.Where(w => w.Parent == lease).Select(w => w.Id).ToArray()) workers.Remove(id);
    }
    public object CreateWorker(Caller caller, JsonObject a)
    {
        Root(caller);
        if (WorkerCount(caller.Parent) >= 128) throw new BridgeException("worker_capacity", "Controller retains 128 workers. Collect results, finish/revoke workers, then call controller_checkpoint to continue.");
        var objective = a.Text("objective", 8000);
        if (a["tools"] is not JsonArray list || list.Count > 64) throw new BridgeException("invalid_arguments", "tools must be an explicit array of at most 64 tool names.");
        var allowed = list.Select(n => n?.GetValue<string>() ?? "").ToHashSet();
        if (allowed.Any(t => !ControllerCatalog.Delegatable(t) || !ControllerCatalog.Allows(caller.Mode, t))) throw new BridgeException("tool_forbidden", "Cannot delegate management/configuration tools or tools from another mode.");
        int? mapId = a["mapId"] == null ? null : a.Number("mapId", 0, int.MaxValue);
        var expiry = clock.GetUtcNow().AddSeconds(a.Number("ttlSeconds", 30, 900, 300));
        if (expiry > caller.Parent.Expires) expiry = caller.Parent.Expires;
        var w = new Worker(caller.Parent, objective, allowed, mapId, expiry); workers.Add(w.Id, w);
        return new { workerId = w.Id, grantToken = w.Token, mode = caller.Mode, objective, tools = allowed, mapId, expiresAt = expiry };
    }
    public Worker GetWorker(Caller caller, JsonObject a)
    {
        var w = caller.Worker ?? (workers.TryGetValue(a.Text("workerId", 64), out var found) ? found : throw new BridgeException("worker_not_found", "Unknown worker."));
        if (w.Parent != caller.Parent) throw new BridgeException("worker_forbidden", "Worker belongs to another controller.");
        return w;
    }
    public static object View(Worker w) => new { workerId = w.Id, mode = w.Parent.Mode, objective = w.Objective, tools = w.Tools, mapId = w.MapId, status = w.Status, expiresAt = w.Expires, result = w.Result };
    public static void Root(Caller caller) { if (caller.Worker != null) throw new BridgeException("controller_only", "Only the parent controller can manage this operation."); }
}
