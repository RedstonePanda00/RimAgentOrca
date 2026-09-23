using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace DeepseekTheOrca
{
    public static class LlmRequestScheduler
    {
        private const int MaxConcurrentRequests = 2;
        private static readonly Queue<WaitingRequest> normalQueue = new Queue<WaitingRequest>();
        private static readonly Queue<WaitingRequest> backgroundQueue = new Queue<WaitingRequest>();
        private static readonly object syncRoot = new object();
        private static readonly Queue<string> pendingDebugMessages = new Queue<string>();
        private static readonly List<string> activeLabels = new List<string>();
        private static int waitingCount;

        public static int WaitingCount
        {
            get
            {
                lock (syncRoot)
                {
                    return waitingCount;
                }
            }
        }

        public static string ActiveLabel
        {
            get
            {
                lock (syncRoot)
                {
                    return string.Join(", ", activeLabels.ToArray());
                }
            }
        }

        public static bool IsBusy
        {
            get { return !ActiveLabel.NullOrEmpty(); }
        }

        // Keep the original signature for already compiled extensions.
        public static Task<T> RunAsync<T>(string label, Func<Task<T>> action)
        {
            return RunAsync(label, action, false, CancellationToken.None);
        }

        public static async Task<T> RunAsync<T>(string label, Func<Task<T>> action, bool background, CancellationToken cancellation = default(CancellationToken))
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }

            using (await EnterAsync(label, background, cancellation).ConfigureAwait(false))
            {
                return await action().ConfigureAwait(false);
            }
        }

        public static async Task RunAsync(string label, Func<Task> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }

            using (await EnterAsync(label).ConfigureAwait(false))
            {
                await action().ConfigureAwait(false);
            }
        }

        private static async Task<IDisposable> EnterAsync(string label, bool background = false, CancellationToken cancellation = default(CancellationToken))
        {
            label = label.NullOrEmpty() ? "LLM request" : label;
            var request = new WaitingRequest { label = label, cancellation = cancellation };
            lock (syncRoot)
            {
                waitingCount++;
                (background ? backgroundQueue : normalQueue).Enqueue(request);
                AdmitWaiting();
            }
            using (cancellation.Register(() =>
            {
                lock (syncRoot) { if (!request.admitted) request.ready.TrySetCanceled(); }
            }))
                return await request.ready.Task.ConfigureAwait(false);
        }

        private sealed class WaitingRequest
        {
            public string label;
            public bool admitted;
            public CancellationToken cancellation;
            public readonly Stopwatch timer = Stopwatch.StartNew();
            public readonly TaskCompletionSource<IDisposable> ready = new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static void AdmitWaiting()
        {
            while (activeLabels.Count < MaxConcurrentRequests && (normalQueue.Count > 0 || backgroundQueue.Count > 0))
            {
                var request = normalQueue.Count > 0 ? normalQueue.Dequeue() : backgroundQueue.Dequeue();
                waitingCount--;
                if (request.cancellation.IsCancellationRequested || request.ready.Task.IsCanceled)
                { request.ready.TrySetCanceled(); continue; }
                request.admitted = true;
                activeLabels.Add(request.label);
                Debug("Started " + request.label + WaitSuffix(request.timer.ElapsedMilliseconds) + ".");
                request.ready.SetResult(new Lease(request.label));
            }
        }

        private static string WaitSuffix(long waitMs)
        {
            return waitMs <= 5 ? "" : " after waiting " + waitMs + " ms";
        }

        private static void Release(string label)
        {
            lock (syncRoot)
            {
                activeLabels.Remove(label);
                AdmitWaiting();
            }

            Debug("Finished " + label + ".");
        }

        private static void Debug(string message)
        {
            if (DeepseekTheOrcaMod.Settings != null && DeepseekTheOrcaMod.Settings.debugLogging)
            {
                lock (syncRoot)
                {
                    pendingDebugMessages.Enqueue(message ?? "");
                    while (pendingDebugMessages.Count > 50)
                    {
                        pendingDebugMessages.Dequeue();
                    }
                }
            }
        }

        public static void Tick()
        {
            if (DeepseekTheOrcaMod.Settings == null || !DeepseekTheOrcaMod.Settings.debugLogging)
            {
                ClearPendingDebugMessages();
                return;
            }

            for (int i = 0; i < 8; i++)
            {
                string message;
                lock (syncRoot)
                {
                    if (pendingDebugMessages.Count == 0)
                    {
                        return;
                    }

                    message = pendingDebugMessages.Dequeue();
                }

                Log.Message("[RimAgent] LLM scheduler: " + message);
            }
        }

        private static void ClearPendingDebugMessages()
        {
            lock (syncRoot)
            {
                pendingDebugMessages.Clear();
            }
        }

        private sealed class Lease : IDisposable
        {
            private readonly string label;
            private bool disposed;

            public Lease(string label)
            {
                this.label = label;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                Release(label);
            }
        }
    }
}
