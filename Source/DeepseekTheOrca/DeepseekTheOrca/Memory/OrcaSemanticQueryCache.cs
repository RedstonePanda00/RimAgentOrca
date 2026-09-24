using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DeepseekTheOrca
{
    // Main-thread cache of network-only tasks shared by memory and tool routing.
    // Callers poll readiness; prompt construction never waits on the network.
    public static class OrcaSemanticQueryCache
    {
        private const int Capacity = 64;
        private static readonly Dictionary<string, Task<OrcaEmbeddingResult>> entries = new Dictionary<string, Task<OrcaEmbeddingResult>>();
        private static readonly Queue<string> order = new Queue<string>();
        private static readonly OrcaEmbeddingClient client = new OrcaEmbeddingClient();

        private static string Key(string query) { return OrcaEmbeddingIdentity.Current + ":" + query; }

        public static Task<OrcaEmbeddingResult> Prepare(DeepseekTheOrcaSettings settings, string query, int timeoutMs)
        {
            if (settings == null || string.IsNullOrWhiteSpace(query) || !settings.HasModelForRole(OrcaLlmModelRole.Embedding)) return null;
            string key = Key(query);
            Task<OrcaEmbeddingResult> task;
            if (entries.TryGetValue(key, out task)) return task;
            if (LlmRequestScheduler.IsBusy) return null;
            task = client.EmbedAsync(settings, query, Math.Max(1000, timeoutMs));
            entries.Add(key, task);
            order.Enqueue(key);
            while (order.Count > Capacity) entries.Remove(order.Dequeue());
            return task;
        }

        public static List<float> Ready(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            Task<OrcaEmbeddingResult> task;
            if (!entries.TryGetValue(Key(query), out task) || !task.IsCompleted || task.IsCanceled || task.IsFaulted) return null;
            var result = task.Result;
            return result != null && result.success && OrcaEmbeddingIdentity.Matches(result.identity, OrcaEmbeddingIdentity.Current) ? result.embedding : null;
        }

        public static void Reset()
        {
            client.CancelQueuedRequests();
            entries.Clear();
            order.Clear();
        }
    }
}
