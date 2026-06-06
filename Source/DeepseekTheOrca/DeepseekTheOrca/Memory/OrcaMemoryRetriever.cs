using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaMemoryRetriever
    {
        private const float MmrRelevanceWeight = 0.65f;
        private const float MmrDiversityWeight = 0.35f;
        private const int MaxAtomicPerCluster = 1;

        public static List<OrcaMemoryRecord> Retrieve(List<OrcaMemoryRecord> records, OrcaMemoryKeywordIndex index, string query, int maxCount)
        {
            return Retrieve(records, index, query, null, maxCount);
        }

        public static List<OrcaMemoryRecord> Retrieve(List<OrcaMemoryRecord> records, OrcaMemoryKeywordIndex index, string query, List<float> queryEmbedding, int maxCount)
        {
            if (records == null || records.Count == 0 || maxCount <= 0)
            {
                return new List<OrcaMemoryRecord>();
            }

            List<string> queryKeywords = OrcaMemoryWriter.QueryKeywords(query);
            List<string> candidateIds = index == null ? new List<string>() : index.FindIds(queryKeywords);
            HashSet<string> idSet = new HashSet<string>(candidateIds);
            string saveId = OrcaLongTermMemoryService.CurrentSaveId();
            long now = OrcaMemoryRecord.NowUnixSeconds();
            bool hasSemanticQuery = queryEmbedding != null && queryEmbedding.Count > 0;

            List<ScoredMemory> scored = new List<ScoredMemory>();
            for (int i = 0; i < records.Count; i++)
            {
                OrcaMemoryRecord record = records[i];
                if (!IsRetrievable(record))
                {
                    continue;
                }

                float score = Score(record, idSet.Contains(record.id), queryKeywords, queryEmbedding, hasSemanticQuery, saveId, now);
                if (score > 0.05f)
                {
                    scored.Add(new ScoredMemory(record, score));
                }
            }

            return SelectDiverse(scored, maxCount);
        }

        private static List<OrcaMemoryRecord> SelectDiverse(List<ScoredMemory> scored, int maxCount)
        {
            List<OrcaMemoryRecord> selected = new List<OrcaMemoryRecord>();
            Dictionary<string, int> selectedByCluster = new Dictionary<string, int>();
            List<ScoredMemory> remaining = scored.OrderByDescending(item => item.score).ToList();

            while (selected.Count < maxCount && remaining.Count > 0)
            {
                ScoredMemory best = null;
                float bestScore = float.MinValue;
                for (int i = 0; i < remaining.Count; i++)
                {
                    ScoredMemory candidate = remaining[i];
                    if (WouldExceedClusterCap(candidate.record, selectedByCluster))
                    {
                        continue;
                    }

                    float diversityPenalty = MaxSimilarity(candidate.record, selected);
                    float mmr = candidate.score * MmrRelevanceWeight - diversityPenalty * MmrDiversityWeight;
                    if (mmr > bestScore)
                    {
                        bestScore = mmr;
                        best = candidate;
                    }
                }

                if (best == null)
                {
                    break;
                }

                remaining.Remove(best);
                selected.Add(best.record);
                string clusterKey = ClusterKey(best.record);
                if (!clusterKey.NullOrEmpty())
                {
                    int count;
                    selectedByCluster.TryGetValue(clusterKey, out count);
                    selectedByCluster[clusterKey] = count + 1;
                }
            }

            return selected;
        }

        private static bool WouldExceedClusterCap(OrcaMemoryRecord record, Dictionary<string, int> selectedByCluster)
        {
            string key = ClusterKey(record);
            if (key.NullOrEmpty())
            {
                return false;
            }

            int count;
            return selectedByCluster.TryGetValue(key, out count) && count >= MaxAtomicPerCluster;
        }

        private static string ClusterKey(OrcaMemoryRecord record)
        {
            if (record == null)
            {
                return "";
            }
            if (record.memoryKind == "cluster")
            {
                return record.id;
            }
            return record.clusterId ?? "";
        }

        private static float MaxSimilarity(OrcaMemoryRecord candidate, List<OrcaMemoryRecord> selected)
        {
            float max = 0f;
            for (int i = 0; i < selected.Count; i++)
            {
                max = Math.Max(max, Similarity(candidate, selected[i]));
            }
            return max;
        }

        private static float Similarity(OrcaMemoryRecord a, OrcaMemoryRecord b)
        {
            if (a == null || b == null)
            {
                return 0f;
            }
            string clusterA = ClusterKey(a);
            string clusterB = ClusterKey(b);
            if (!clusterA.NullOrEmpty() && clusterA == clusterB)
            {
                return 0.85f;
            }

            return Cosine(a.centroidEmbedding, b.centroidEmbedding);
        }

        private static float Score(OrcaMemoryRecord record, bool keywordHit, List<string> queryKeywords, List<float> queryEmbedding, bool hasSemanticQuery, string saveId, long now)
        {
            float keywordScore = keywordHit ? 0.45f : KeywordOverlap(record, queryKeywords) * 0.22f;
            float semanticScore = hasSemanticQuery ? Math.Max(0f, Cosine(record.centroidEmbedding, queryEmbedding)) * 0.55f : 0f;
            float score = Math.Max(keywordScore, semanticScore);
            score += Math.Min(0.18f, Math.Max(0f, record.importance) * 0.18f);
            if (!saveId.NullOrEmpty() && record.saveIds != null && record.saveIds.Contains(saveId))
            {
                score += 0.14f;
            }

            score += Math.Min(0.1f, Math.Max(0, record.occurrenceCount - 1) * 0.02f);
            score += Math.Min(0.06f, Math.Max(0f, record.strength) * 0.06f);
            long age = Math.Max(0, now - Math.Max(record.lastAccessed, record.createdAt));
            score += (float)Math.Max(0.0, 0.1 - age / 6048000.0);
            if (record.memoryKind == "cluster")
            {
                score += 0.04f;
            }
            return score;
        }

        private static float KeywordOverlap(OrcaMemoryRecord record, List<string> queryKeywords)
        {
            if (record.keywords == null || queryKeywords == null || queryKeywords.Count == 0)
            {
                return 0f;
            }

            int hits = 0;
            for (int i = 0; i < queryKeywords.Count; i++)
            {
                if (record.keywords.Contains(queryKeywords[i]))
                {
                    hits++;
                }
            }

            return Math.Min(1f, hits / 3f);
        }

        private static bool IsRetrievable(OrcaMemoryRecord record)
        {
            if (record == null || record.DisplayText.NullOrEmpty() || record.consolidationState == "compressed" || record.consolidationState == "pruned")
            {
                return false;
            }

            return record.memoryKind == "cluster" || record.embeddingState == "ready";
        }

        public static float Cosine(List<float> a, List<float> b)
        {
            if (a == null || b == null || a.Count != b.Count || a.Count == 0)
            {
                return 0f;
            }

            double dot = 0.0;
            double lenA = 0.0;
            double lenB = 0.0;
            for (int i = 0; i < a.Count; i++)
            {
                dot += a[i] * b[i];
                lenA += a[i] * a[i];
                lenB += b[i] * b[i];
            }

            return lenA <= 0.0 || lenB <= 0.0 ? 0f : (float)(dot / (Math.Sqrt(lenA) * Math.Sqrt(lenB)));
        }

        private sealed class ScoredMemory
        {
            public readonly OrcaMemoryRecord record;
            public readonly float score;

            public ScoredMemory(OrcaMemoryRecord record, float score)
            {
                this.record = record;
                this.score = score;
            }
        }
    }
}
