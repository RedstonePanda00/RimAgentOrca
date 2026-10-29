using System.Text.Json.Nodes;

namespace RimAgent.Core;

public sealed partial class AgentRuntime
{
    private sealed record CheckpointReceipt(int Epoch, JsonObject Arguments, ToolReply Reply);
    private readonly Dictionary<string, CheckpointReceipt> checkpoints = [];

    private object History(ControllerAccess.Lease lease) => new {
        historyEpoch = lease.HistoryEpoch,
        receipts = receipts.Values.Count(r => r.LeaseId == lease.Token), receiptLimit = 1024,
        tasks = jobs.Values.Count(j => j.LeaseId == lease.Token), taskLimit = 128,
        workers = access.WorkerCount(lease), workerLimit = 128
    };

    private ToolReply Checkpoint(ControllerAccess.Caller caller, JsonObject args)
    {
        ControllerAccess.Root(caller);
        var lease = caller.Parent;
        int epoch = args.Number("historyEpoch", 0, int.MaxValue);
        _ = args.Text("requestId", 128);
        if (checkpoints.TryGetValue(lease.Token, out var prior) && prior.Epoch == epoch)
        {
            if (!JsonNode.DeepEquals(prior.Arguments, args))
                throw new BridgeException("request_conflict", "A different checkpoint already acknowledged this epoch.");
            return prior.Reply;
        }
        if (epoch != lease.HistoryEpoch || epoch == int.MaxValue)
            throw new BridgeException("history_epoch", "Use the current historyEpoch from controller_renew. Old checkpoints cannot be replayed.");
        if (jobs.Values.Any(j => j.LeaseId == lease.Token && j.Status == "running") || access.HasRunningWorkers(lease))
            throw new BridgeException("history_busy", "Finish or cancel running tasks and complete/revoke workers before acknowledging their history.");
        var discarded = History(lease);
        ClearHistory(lease.Token);
        access.ClearWorkers(lease);
        lease.HistoryEpoch++;
        var reply = ToolReply.Ok(new { historyEpoch = lease.HistoryEpoch, discarded,
            note = "Completed task/worker results and mutation receipts were acknowledged and removed. Use this epoch for NEW mutations only." });
        checkpoints[lease.Token] = new(epoch, (JsonObject)args.DeepClone(), reply);
        Emit("controller_checkpoint_" + lease.Mode);
        return reply;
    }

    private void ClearHistory(string leaseId)
    {
        foreach (var key in receipts.Where(p => p.Value.LeaseId == leaseId).Select(p => p.Key).ToArray()) receipts.Remove(key);
        foreach (var job in jobs.Values.Where(j => j.LeaseId == leaseId).ToArray()) { Retire(job); jobs.Remove(job.Id); }
        checkpoints.Remove(leaseId);
    }

    private void CheckTaskCapacity(ControllerAccess.Lease lease)
    {
        if (jobs.Values.Count(j => j.Status == "running") >= 4)
            throw new BridgeException("task_capacity", "At most four active tasks; wait for completion or cancel one.");
        if (jobs.Values.Count(j => j.LeaseId == lease.Token) >= 128)
            throw new BridgeException("task_capacity", "Controller retains 128 tasks. Read results, finish/cancel active tasks and workers, then call controller_checkpoint to continue.");
    }
}
