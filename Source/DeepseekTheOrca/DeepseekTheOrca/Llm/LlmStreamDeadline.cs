using System;
using System.Threading;
using System.Threading.Tasks;

namespace DeepseekTheOrca
{
    internal static class LlmStreamDeadline
    {
        // StreamReader on RimWorld's runtime has no cancellation-aware ReadLineAsync.
        // The caller disposes the HTTP response/stream when this wait is cancelled.
        internal static async Task<T> Await<T>(Task<T> operation, CancellationToken token)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(operation, cancelled.Task).ConfigureAwait(false) != operation)
                {
                    ObserveFailure(operation);
                    throw new OperationCanceledException(token);
                }
                token.ThrowIfCancellationRequested();
                return await operation.ConfigureAwait(false);
            }
        }

        private static async void ObserveFailure(Task operation)
        {
            try { await operation.ConfigureAwait(false); }
            catch { /* The request was already cancelled and its transport disposed. */ }
        }
    }
}
