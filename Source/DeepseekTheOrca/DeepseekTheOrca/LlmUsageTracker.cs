using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepseekTheOrca
{
    public sealed class LlmUsageSample
    {
        public DateTime timeUtc;
        public int promptTokens;
        public int completionTokens;
        public int totalTokens;
        public int cacheHitTokens;
        public int cacheMissTokens;
        public int elapsedMs;
    }

    public sealed class LlmUsageSummary
    {
        public int totalCalls;
        public int totalTokens;
        public int cacheHitTokens;
        public int cacheMissTokens;
        public int maxTokens;
        public float averageTokens;
        public float cacheHitRate;
        public float averageElapsedMs;
    }

    public static class LlmUsageTracker
    {
        private const int MaxSamples = 120;
        private static readonly object syncRoot = new object();
        private static readonly List<LlmUsageSample> samples = new List<LlmUsageSample>();

        public static void Record(LlmChatResponse response)
        {
            if (response == null || !response.success)
            {
                return;
            }

            lock (syncRoot)
            {
                samples.Add(new LlmUsageSample
                {
                    timeUtc = DateTime.UtcNow,
                    promptTokens = response.promptTokens,
                    completionTokens = response.completionTokens,
                    totalTokens = response.totalTokens,
                    cacheHitTokens = response.cacheHitTokens,
                    cacheMissTokens = response.cacheMissTokens,
                    elapsedMs = response.elapsedMs
                });

                while (samples.Count > MaxSamples)
                {
                    samples.RemoveAt(0);
                }
            }
        }

        public static List<LlmUsageSample> Snapshot()
        {
            lock (syncRoot)
            {
                return samples.Select(sample => new LlmUsageSample
                {
                    timeUtc = sample.timeUtc,
                    promptTokens = sample.promptTokens,
                    completionTokens = sample.completionTokens,
                    totalTokens = sample.totalTokens,
                    cacheHitTokens = sample.cacheHitTokens,
                    cacheMissTokens = sample.cacheMissTokens,
                    elapsedMs = sample.elapsedMs
                }).ToList();
            }
        }

        public static LlmUsageSummary Summary()
        {
            lock (syncRoot)
            {
                LlmUsageSummary summary = new LlmUsageSummary();
                summary.totalCalls = samples.Count;
                if (samples.Count == 0)
                {
                    return summary;
                }

                summary.totalTokens = samples.Sum(sample => sample.totalTokens);
                summary.cacheHitTokens = samples.Sum(sample => sample.cacheHitTokens);
                summary.cacheMissTokens = samples.Sum(sample => sample.cacheMissTokens);
                int cachePromptTokens = summary.cacheHitTokens + summary.cacheMissTokens;
                if (cachePromptTokens > 0)
                {
                    summary.cacheHitRate = (float)summary.cacheHitTokens / cachePromptTokens;
                }

                summary.maxTokens = samples.Max(sample => sample.totalTokens);
                summary.averageTokens = (float)samples.Average(sample => sample.totalTokens);
                summary.averageElapsedMs = (float)samples.Average(sample => sample.elapsedMs);
                return summary;
            }
        }
    }
}
