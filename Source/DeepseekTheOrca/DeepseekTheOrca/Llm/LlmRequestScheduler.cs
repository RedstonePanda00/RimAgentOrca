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
        private static readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private static readonly object syncRoot = new object();
        private static readonly Queue<string> pendingDebugMessages = new Queue<string>();
        private static int waitingCount;
        private static string activeLabel = "";

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
                    return activeLabel;
                }
            }
        }

        public static bool IsBusy
        {
            get { return !ActiveLabel.NullOrEmpty(); }
        }

        public static async Task<T> RunAsync<T>(string label, Func<Task<T>> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }

            using (await EnterAsync(label).ConfigureAwait(false))
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

        private static async Task<IDisposable> EnterAsync(string label)
        {
            label = label.NullOrEmpty() ? "LLM request" : label;
            Stopwatch waitTimer = Stopwatch.StartNew();
            lock (syncRoot)
            {
                waitingCount++;
            }

            await gate.WaitAsync().ConfigureAwait(false);

            waitTimer.Stop();
            lock (syncRoot)
            {
                waitingCount--;
                activeLabel = label;
            }

            Debug("Started " + label + WaitSuffix(waitTimer.ElapsedMilliseconds) + ".");
            return new Lease(label);
        }

        private static string WaitSuffix(long waitMs)
        {
            return waitMs <= 5 ? "" : " after waiting " + waitMs + " ms";
        }

        private static void Release(string label)
        {
            lock (syncRoot)
            {
                activeLabel = "";
            }

            Debug("Finished " + label + ".");
            gate.Release();
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
