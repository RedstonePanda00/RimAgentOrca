using System.Text.Json;
using System.Text.Json.Nodes;

namespace RimAgent.Core;

public static class WireJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static JsonNode? Node(object? value) => JsonSerializer.SerializeToNode(value, Options);
}

public sealed record BridgeError(string Code, string Message);
public sealed record ToolReply(bool Success, JsonNode? Data = null, BridgeError? Error = null)
{
    public static ToolReply Ok(object? data) => new(true, WireJson.Node(data));
    public static ToolReply Fail(string code, string message) => new(false, null, new(code, message));
}
public sealed class BridgeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
public sealed record AgentDescriptor(string Id, string Role, string Model, bool Available);
public sealed record AnalysisResult(string Text, string Model, int InputTokens, int OutputTokens);
public sealed record RuntimeEvent(long Cursor, string Kind, string? TaskId, DateTimeOffset At);

// Implementations capture game objects on the main thread. Returned tasks own detached data only.
public interface IAgentGame
{
    object GetState();
    object CaptureColony(int mapId);
    IReadOnlyList<AgentDescriptor> ListAgents();
    Task<AnalysisResult> Analyze(string agentId, string instruction, object snapshot, int maxTokens, CancellationToken cancellation);
    object SetPaused(bool paused);
    object SetDrafted(int mapId, string pawnId, bool drafted);
}

public interface IAgentOperations
{
    object Read(string tool, JsonObject arguments);
    object Act(string tool, JsonObject arguments);
    Task<object> BeginOperation(string tool, JsonObject arguments, CancellationToken cancellation);
    void UpdateOperations();
    void ReleaseOperations();
}

public interface IAgentRelay
{
    void ValidateRelayResult(string kind, int? mapId, JsonObject context, JsonObject result);
}

public static class Arguments
{
    public static string Text(this JsonObject args, string key, int max = 4096)
    {
        if (args[key] is not JsonValue v || !v.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text) || text.Length > max)
            throw new BridgeException("invalid_arguments", $"{key} must be a nonempty string of at most {max} characters.");
        return text;
    }
    public static int Number(this JsonObject args, string key, int min, int max, int? fallback = null)
    {
        if (args[key] == null && fallback.HasValue) return fallback.Value;
        if (args[key] is not JsonValue v || !v.TryGetValue<int>(out var n) || n < min || n > max)
            throw new BridgeException("invalid_arguments", $"{key} must be an integer between {min} and {max}.");
        return n;
    }
    public static bool Flag(this JsonObject args, string key)
    {
        if (args[key] is not JsonValue v || !v.TryGetValue<bool>(out var b))
            throw new BridgeException("invalid_arguments", $"{key} must be a boolean.");
        return b;
    }
}
