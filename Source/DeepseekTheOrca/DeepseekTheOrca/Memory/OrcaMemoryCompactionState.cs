using System;

namespace DeepseekTheOrca
{
    // Pure retry state. Timestamps are UTC wall-clock seconds, never game ticks.
    internal sealed class OrcaMemoryCompactionState
    {
        internal const int MaxAttempts = 3;
        internal int attempts;
        internal long retryAt;
        internal bool inFlight;
        internal string error = "";

        internal bool Paused { get { return !inFlight && attempts >= MaxAttempts; } }
        internal bool CanStart(long now) { return !inFlight && attempts < MaxAttempts && now >= retryAt; }

        internal void Begin(long now)
        {
            if (!CanStart(now)) throw new InvalidOperationException("Memory compaction is paused or waiting to retry.");
            attempts++;
            inFlight = true;
        }

        internal void Fail(long now, string message)
        {
            inFlight = false;
            error = message ?? "Unknown failure";
            retryAt = attempts >= MaxAttempts ? 0 : now + (attempts <= 1 ? 30 : 120);
        }

        internal void CancelQueued()
        {
            if (inFlight) attempts = Math.Max(0, attempts - 1);
            inFlight = false;
        }

        internal void Reset()
        {
            attempts = 0;
            retryAt = 0;
            inFlight = false;
            error = "";
        }

        internal void Pause(string message)
        {
            inFlight = false;
            attempts = MaxAttempts;
            retryAt = 0;
            error = message ?? "Memory storage failed";
        }
    }
}
