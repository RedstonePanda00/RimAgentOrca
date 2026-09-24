using System;
using System.Threading.Tasks;

namespace DeepseekTheOrca
{
    public sealed partial class OrcaChatSession
    {
        private Task<OrcaEmbeddingResult> pendingSemanticQuery;
        private Action afterSemanticQuery;
        private DateTime semanticQueryDeadline;

        private void PrepareSemanticQuery(DeepseekTheOrcaSettings settings, Action continuation)
        {
            int wait = 0;
            if (settings.enableLongTermMemory && settings.enableSemanticMemoryQuery) wait = Math.Max(wait, settings.semanticMemoryQueryWaitMs);
            if (settings.enableSemanticToolSearch) wait = Math.Max(wait, settings.toolSemanticSearchWaitMs);
            if (wait <= 0) { continuation(); return; }
            pendingSemanticQuery = OrcaSemanticQueryCache.Prepare(settings, lastUserText,
                Math.Max(settings.semanticMemoryQueryHardTimeoutMs, wait));
            if (pendingSemanticQuery == null || pendingSemanticQuery.IsCompleted) { pendingSemanticQuery = null; continuation(); return; }
            semanticQueryDeadline = DateTime.UtcNow.AddMilliseconds(wait);
            afterSemanticQuery = continuation;
        }

        private void TickSemanticQuery()
        {
            if (afterSemanticQuery == null) return;
            if (pendingSemanticQuery != null && !pendingSemanticQuery.IsCompleted && DateTime.UtcNow < semanticQueryDeadline) return;
            var continuation = afterSemanticQuery;
            afterSemanticQuery = null;
            pendingSemanticQuery = null;
            continuation();
        }
    }
}
