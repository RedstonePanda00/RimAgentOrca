using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaToolBundleRouter
    {
        private const int MaxSemanticPrewarmIntervalTicks = 250;
        private static readonly object syncRoot = new object();
        private static readonly Dictionary<string, List<float>> bundleEmbeddings = new Dictionary<string, List<float>>();
        private static readonly OrcaEmbeddingClient embeddingClient = new OrcaEmbeddingClient();
        private static Task<OrcaEmbeddingResult> pendingBundleEmbedding;
        private static string pendingBundleId = "";
        private static int lastPrewarmTick = -MaxSemanticPrewarmIntervalTicks;

        public static HashSet<string> SelectToolNames(string query, OrcaLlmModelRole role, bool allowExecutionTools)
        {
            List<OrcaToolBundleDef> bundles = BundlesForRole(role);
            HashSet<string> selected = new HashSet<string>();
            for (int i = 0; i < bundles.Count; i++)
            {
                OrcaToolBundleDef bundle = bundles[i];
                if (bundle.includeByDefault && BundleAllowed(bundle, allowExecutionTools, query))
                {
                    AddTools(selected, bundle);
                }
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            int topK = settings == null ? 5 : settings.toolSearchTopK;
            List<ScoredBundle> scored = ScoreBundles(bundles, query, allowExecutionTools, settings);
            foreach (ScoredBundle item in scored.OrderByDescending(item => item.score).Take(topK))
            {
                if (item.score > 0f)
                {
                    AddTools(selected, item.bundle);
                }
            }

            if (selected.Count == 0)
            {
                for (int i = 0; i < bundles.Count; i++)
                {
                    if (bundles[i].includeByDefault && BundleAllowed(bundles[i], allowExecutionTools, query))
                    {
                        AddTools(selected, bundles[i]);
                    }
                }
            }

            return selected;
        }

        public static void Tick()
        {
            CompletePendingBundleEmbedding();
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !settings.enableSemanticToolSearch || !settings.HasModelForRole(OrcaLlmModelRole.Embedding))
            {
                return;
            }
            if (LlmRequestScheduler.IsBusy || pendingBundleEmbedding != null)
            {
                return;
            }

            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            if (tick - lastPrewarmTick < MaxSemanticPrewarmIntervalTicks)
            {
                return;
            }

            OrcaToolBundleDef next = BundlesForRole(OrcaLlmModelRole.Tool).FirstOrDefault(bundle => !HasEmbedding(bundle.BundleId));
            if (next == null)
            {
                return;
            }

            lastPrewarmTick = tick;
            pendingBundleId = next.BundleId;
            pendingBundleEmbedding = embeddingClient.EmbedAsync(settings, next.SemanticText, 2500);
        }

        private static void CompletePendingBundleEmbedding()
        {
            Task<OrcaEmbeddingResult> task = pendingBundleEmbedding;
            if (task == null || !task.IsCompleted)
            {
                return;
            }

            try
            {
                OrcaEmbeddingResult result = task.Result;
                if (result != null && result.success && result.embedding != null && result.embedding.Count > 0)
                {
                    lock (syncRoot)
                    {
                        bundleEmbeddings[pendingBundleId] = result.embedding;
                    }
                }
            }
            catch
            {
            }

            pendingBundleEmbedding = null;
            pendingBundleId = "";
        }

        private static List<ScoredBundle> ScoreBundles(List<OrcaToolBundleDef> bundles, string query, bool allowExecutionTools, DeepseekTheOrcaSettings settings)
        {
            List<ScoredBundle> scored = new List<ScoredBundle>();
            List<float> queryEmbedding = TryEmbedQuery(settings, query);
            for (int i = 0; i < bundles.Count; i++)
            {
                OrcaToolBundleDef bundle = bundles[i];
                if (!bundle.topKEligible || !BundleAllowed(bundle, allowExecutionTools, query))
                {
                    continue;
                }

                float fallbackScore = FallbackScore(bundle, query);
                float semanticScore = SemanticScore(bundle, queryEmbedding);
                float score = Math.Max(fallbackScore, semanticScore) + Math.Max(0f, bundle.priority) * 0.01f;
                scored.Add(new ScoredBundle(bundle, score));
            }

            return scored;
        }

        private static List<float> TryEmbedQuery(DeepseekTheOrcaSettings settings, string query)
        {
            if (settings == null || !settings.enableSemanticToolSearch || query.NullOrEmpty() || !settings.HasModelForRole(OrcaLlmModelRole.Embedding))
            {
                return null;
            }
            if (LlmRequestScheduler.IsBusy || !AnyBundleEmbeddingReady())
            {
                return null;
            }

            try
            {
                int waitMs = Math.Max(0, settings.toolSemanticSearchWaitMs);
                if (waitMs <= 0)
                {
                    return null;
                }

                Task<OrcaEmbeddingResult> task = embeddingClient.EmbedAsync(settings, query, waitMs);
                if (!task.Wait(waitMs))
                {
                    return null;
                }

                OrcaEmbeddingResult result = task.Result;
                return result != null && result.success && result.embedding != null && result.embedding.Count > 0 ? result.embedding : null;
            }
            catch
            {
                return null;
            }
        }

        private static float SemanticScore(OrcaToolBundleDef bundle, List<float> queryEmbedding)
        {
            if (bundle == null || queryEmbedding == null || queryEmbedding.Count == 0)
            {
                return 0f;
            }

            List<float> bundleEmbedding;
            lock (syncRoot)
            {
                bundleEmbeddings.TryGetValue(bundle.BundleId, out bundleEmbedding);
            }

            float cosine = OrcaMemoryRetriever.Cosine(bundleEmbedding, queryEmbedding);
            return cosine <= 0f ? 0f : cosine * 1.2f;
        }

        private static float FallbackScore(OrcaToolBundleDef bundle, string query)
        {
            if (bundle == null || query.NullOrEmpty())
            {
                return bundle != null && bundle.includeByDefault ? 0.05f : 0f;
            }

            string lower = query.ToLowerInvariant();
            float score = 0f;
            foreach (string alias in bundle.aliases ?? new List<string>())
            {
                if (!alias.NullOrEmpty() && lower.Contains(alias.ToLowerInvariant()))
                {
                    score += 1.25f;
                }
            }

            if (!bundle.label.NullOrEmpty() && lower.Contains(bundle.label.ToLowerInvariant()))
            {
                score += 0.6f;
            }

            string description = bundle.description ?? "";
            List<string> queryTokens = SimpleTokens(lower);
            for (int i = 0; i < queryTokens.Count; i++)
            {
                string token = queryTokens[i];
                if (token.Length >= 3 && description.ToLowerInvariant().Contains(token))
                {
                    score += 0.15f;
                }
            }

            return score;
        }

        private static bool BundleAllowed(OrcaToolBundleDef bundle, bool allowExecutionTools, string query)
        {
            if (bundle == null)
            {
                return false;
            }
            if (!allowExecutionTools && !bundle.allowDuringProactive)
            {
                return false;
            }
            if (bundle.requiresExplicitIntent && !HasExplicitExecutionIntent(query))
            {
                return false;
            }
            return true;
        }

        private static bool HasExplicitExecutionIntent(string query)
        {
            string lower = (query ?? "").ToLowerInvariant();
            return ContainsAny(lower, "trigger", "spawn", "schedule", "execute", "fire incident", "raid now", "生成", "召唤", "执行", "触发", "安排", "袭击");
        }

        private static List<OrcaToolBundleDef> BundlesForRole(OrcaLlmModelRole role)
        {
            List<OrcaToolBundleDef> defs = DefDatabase<OrcaToolBundleDef>.AllDefsListForReading ?? new List<OrcaToolBundleDef>();
            return defs.Where(bundle => bundle != null && bundle.ExposesToRole(role)).OrderByDescending(bundle => bundle.priority).ToList();
        }

        private static void AddTools(HashSet<string> selected, OrcaToolBundleDef bundle)
        {
            foreach (string toolName in bundle.toolNames ?? new List<string>())
            {
                if (!toolName.NullOrEmpty())
                {
                    selected.Add(toolName.Trim());
                }
            }
        }

        private static bool AnyBundleEmbeddingReady()
        {
            lock (syncRoot)
            {
                return bundleEmbeddings.Count > 0;
            }
        }

        private static bool HasEmbedding(string bundleId)
        {
            lock (syncRoot)
            {
                return bundleEmbeddings.ContainsKey(bundleId);
            }
        }

        private static List<string> SimpleTokens(string text)
        {
            return (text ?? "").Split(' ', '\n', '\r', '\t', ',', '.', ';', ':', '?', '!', '，', '。', '？', '！')
                .Select(item => item.Trim().ToLowerInvariant())
                .Where(item => !item.NullOrEmpty())
                .Distinct()
                .ToList();
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (text.Contains(needles[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private sealed class ScoredBundle
        {
            public readonly OrcaToolBundleDef bundle;
            public readonly float score;

            public ScoredBundle(OrcaToolBundleDef bundle, float score)
            {
                this.bundle = bundle;
                this.score = score;
            }
        }
    }
}
