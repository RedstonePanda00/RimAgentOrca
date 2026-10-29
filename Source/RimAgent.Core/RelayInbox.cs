using System.Text.Json.Nodes;

namespace RimAgent.Core;

public sealed class RelayRequest(string kind, int? mapId, JsonNode? context, DateTimeOffset expires, Action<JsonObject>? validate = null)
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Kind { get; } = kind;
    public int? MapId { get; } = mapId;
    public JsonNode? Context { get; } = context;
    public DateTimeOffset Expires { get; } = expires;
    internal TaskCompletionSource<JsonObject> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<JsonObject> Result => Completion.Task;
    public void Cancel() => Completion.TrySetCanceled();
    public bool TryComplete(JsonObject result)
    {
        if (Result.IsCompleted) return false;
        validate?.Invoke(result);
        return Completion.TrySetResult((JsonObject)result.DeepClone());
    }
    public object View() => new { messageId = Id, kind = Kind, mapId = MapId, context = Context, expiresAt = Expires, status = Result.IsCanceled ? "cancelled" : Result.IsCompleted ? "completed" : "pending" };
}

internal sealed class RelayInbox(TimeProvider clock)
{
    private readonly List<RelayRequest> requests = [];
    public RelayRequest Add(string kind, int? mapId, object context, Action<JsonObject>? validate = null)
    {
        Expire();
        if (requests.Count(r => !r.Result.IsCompleted) >= 32) throw new BridgeException("inbox_full", "External narrator has 32 pending requests.");
        while (requests.Count >= 128) requests.Remove(requests.First(r => r.Result.IsCompleted));
        var request = new RelayRequest(kind, mapId, WireJson.Node(context), clock.GetUtcNow().AddMinutes(10), validate);
        requests.Add(request); return request;
    }
    public object Read() { Expire(); return new { items = requests.Where(r => !r.Result.IsCompleted).Select(r => r.View()).ToArray() }; }
    public RelayRequest Get(string id) => requests.FirstOrDefault(r => r.Id == id) ?? throw new BridgeException("request_not_found", "Unknown relay message.");
    public void Expire() { foreach (var r in requests.Where(r => !r.Result.IsCompleted && r.Expires <= clock.GetUtcNow())) r.Completion.TrySetCanceled(); }
    public void Cancel() { foreach (var r in requests) r.Completion.TrySetCanceled(); }
    public void Clear() { Cancel(); requests.Clear(); }
}
