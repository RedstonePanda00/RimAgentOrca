using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace RimAgent.Core;

public sealed class CommandDispatcher
{
    private readonly Channel<Command> queue = Channel.CreateBounded<Command>(new BoundedChannelOptions(64) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    public async Task<ToolReply> DispatchAsync(string name, JsonObject arguments, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var command = new Command(name, (JsonObject)arguments.DeepClone(), cancellation);
        if (!queue.Writer.TryWrite(command)) return ToolReply.Fail("queue_full", "Game command queue is full or closed.");
        return await command.Completion.Task.WaitAsync(cancellation).ConfigureAwait(false);
    }
    public void Pump(Func<string, JsonObject, ToolReply> execute)
    {
        var timer = Stopwatch.StartNew();
        for (int count = 0; count < 8 && timer.Elapsed.TotalMilliseconds < 2 && queue.Reader.TryRead(out var command); count++)
        {
            if (command.Cancellation.IsCancellationRequested) { command.Completion.TrySetCanceled(command.Cancellation); continue; }
            try { command.Completion.TrySetResult(execute(command.Name, command.Arguments)); }
            catch (Exception ex) { command.Completion.TrySetResult(ToolReply.Fail("execution_failed", ex.Message)); }
        }
    }
    public void Close()
    {
        queue.Writer.TryComplete();
        while (queue.Reader.TryRead(out var command)) command.Completion.TrySetResult(ToolReply.Fail("bridge_closed", "Bridge is disabled."));
    }
    private sealed record Command(string Name, JsonObject Arguments, CancellationToken Cancellation)
    {
        public TaskCompletionSource<ToolReply> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
