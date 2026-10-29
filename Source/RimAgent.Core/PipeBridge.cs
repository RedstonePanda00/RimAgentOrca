using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RimAgent.Core;

public static class BridgeWire
{
    public const int MaximumMessageBytes = 1024 * 1024;
    public static string DefaultPipeName => "RimAgent-" + Environment.UserName;
    public static async Task WriteAsync(Stream stream, string text, CancellationToken cancellation)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > MaximumMessageBytes) throw new IOException("Bridge message exceeds 1 MiB.");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), cancellation).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellation).ConfigureAwait(false);
        await stream.FlushAsync(cancellation).ConfigureAwait(false);
    }
    public static async Task<string> ReadAsync(Stream stream, CancellationToken cancellation)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
        int length = BitConverter.ToInt32(header);
        if (length < 1 || length > MaximumMessageBytes) throw new IOException("Invalid bridge frame length.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellation).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }
    public static async Task<ToolReply> CallAsync(string pipeName, string tool, JsonObject args, CancellationToken cancellation)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(3000, cancellation).ConfigureAwait(false);
        await WriteAsync(pipe, new JsonObject { ["tool"] = tool, ["arguments"] = args.DeepClone() }.ToJsonString(), cancellation).ConfigureAwait(false);
        var reply = await ReadAsync(pipe, cancellation).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ToolReply>(reply, WireJson.Options) ?? throw new IOException("Empty bridge response.");
    }
}

public sealed class PipeBridge : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task[] listeners;
    public PipeBridge(string pipeName, CommandDispatcher dispatcher, Action<string>? diagnostic = null)
    {
        listeners = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(20));
                    var input = JsonNode.Parse(await BridgeWire.ReadAsync(pipe, deadline.Token).ConfigureAwait(false)) as JsonObject
                        ?? throw new JsonException("Expected object.");
                    ToolReply result;
                    try
                    {
                        result = await dispatcher.DispatchAsync(input.Text("tool", 128), input["arguments"] as JsonObject ?? new(), deadline.Token).ConfigureAwait(false);
                    }
                    catch (BridgeException ex) { result = ToolReply.Fail(ex.Code, ex.Message); }
                    await BridgeWire.WriteAsync(pipe, JsonSerializer.Serialize(result, WireJson.Options), deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    diagnostic?.Invoke(ex.GetType().Name + ": " + ex.Message);
                    try { await Task.Delay(250, stop.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
                }
            }
        })).ToArray();
    }
    public void Dispose()
    {
        stop.Cancel();
        _ = Task.WhenAll(listeners).ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
