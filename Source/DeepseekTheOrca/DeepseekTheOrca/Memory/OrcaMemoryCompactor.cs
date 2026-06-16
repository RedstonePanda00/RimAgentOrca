using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace DeepseekTheOrca
{
    // Compaction, clustering, and consolidation policy for long-term memory.
    // Operates on the record list owned by OrcaLongTermMemoryService; callers
    // are responsible for locking and persistence.
    public static class OrcaMemoryCompactor
    {
        public const int ChunkSoftLimit = 500;
        public const int ChunkHardConsolidationThreshold = 700;
        public const int ChunkHardTrimLimit = 900;
        public const int ClusterSoftLimit = 100;
        public const int ClusterChunkSoftLimit = 12;
        public const int ClusterChunkHardConsolidationThreshold = 20;
        public const float ImportantCompressionThreshold = 0.75f;

        public static List<LlmChatMessage> BuildCompactionMessages(List<OrcaRecentExperienceRecord> experiences)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Compress the following recent RimWorld agent experience into long-term-memory-friendly notes.");
            builder.AppendLine("Preserve player preferences, promises, major events, important entities, relationship or emotional changes, tool execution results, and recurring themes.");
            builder.AppendLine("Do not write generic summaries. Prefer concise bullets with enough context for future semantic retrieval.");
            builder.AppendLine();
            for (int i = 0; i < experiences.Count; i++)
            {
                OrcaRecentExperienceRecord record = experiences[i];
                builder.Append("[");
                builder.Append(i + 1);
                builder.Append("] ");
                builder.Append(record.source);
                builder.Append(": ");
                builder.AppendLine(record.text);
            }

            return new List<LlmChatMessage>
            {
                LlmChatMessage.System("You are a memory compaction worker. Return only compact memory notes, no preface."),
                LlmChatMessage.User(builder.ToString())
            };
        }

        public static string BuildLocalCompactionSummary(List<OrcaRecentExperienceRecord> experiences)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Recent experience summary:");
            for (int i = 0; i < experiences.Count; i++)
            {
                OrcaRecentExperienceRecord record = experiences[i];
                builder.Append("- ");
                builder.Append(record.source);
                builder.Append(": ");
                builder.AppendLine(Clamp(record.text, 500));
            }
            return builder.ToString();
        }

        public static List<OrcaMemoryRecord> BuildChunks(string summary, List<OrcaRecentExperienceRecord> compacted)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            int chunkTokens = settings == null ? 450 : settings.memoryChunkTokenSize;
            int overlapTokens = settings == null ? 80 : settings.memoryChunkOverlapTokens;
            string sourceRange = compacted.First().id + ".." + compacted.Last().id;
            List<OrcaMemoryRecord> result = new List<OrcaMemoryRecord>();
            List<string> chunks = OrcaTokenEstimator.Chunk(summary, chunkTokens, overlapTokens);
            for (int i = 0; i < chunks.Count; i++)
            {
                string text = chunks[i].Trim();
                if (!text.NullOrEmpty())
                {
                    result.Add(OrcaMemoryWriter.BuildChunkRecord("context_compaction", text, sourceRange));
                }
            }

            return result;
        }

        public static int EstimateRecentTokens(List<OrcaRecentExperienceRecord> experiences)
        {
            int tokens = 0;
            for (int i = 0; i < experiences.Count; i++)
            {
                tokens += experiences[i] == null || experiences[i].text == null ? 0 : OrcaTokenEstimator.Estimate(experiences[i].text);
            }
            return tokens;
        }

        public static void AttachChunkToCluster(List<OrcaMemoryRecord> records, OrcaMemoryRecord chunk)
        {
            OrcaMemoryRecord cluster = BestClusterTarget(records, chunk.centroidEmbedding);
            if (cluster == null)
            {
                cluster = CreateClusterFromChunk(chunk);
                records.Add(cluster);
            }
            else
            {
                AddChunkToCluster(cluster, chunk);
            }

            chunk.clusterId = cluster.id;
        }

        private static OrcaMemoryRecord BestClusterTarget(List<OrcaMemoryRecord> records, List<float> sourceEmbedding)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            float threshold = settings == null ? 0.9f : settings.memoryMergeCosineThreshold;
            OrcaMemoryRecord best = null;
            float bestScore = threshold;
            for (int i = 0; i < records.Count; i++)
            {
                OrcaMemoryRecord candidate = records[i];
                if (candidate == null || candidate.memoryKind != "cluster" || candidate.centroidEmbedding == null || candidate.centroidEmbedding.Count != sourceEmbedding.Count)
                {
                    continue;
                }

                float score = OrcaMemoryRetriever.Cosine(candidate.centroidEmbedding, sourceEmbedding);
                if (score >= bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private static OrcaMemoryRecord CreateClusterFromChunk(OrcaMemoryRecord chunk)
        {
            long now = OrcaMemoryRecord.NowUnixSeconds();
            return new OrcaMemoryRecord
            {
                id = Guid.NewGuid().ToString("N"),
                personaId = chunk.personaId,
                saveIds = MergeList(null, chunk.saveIds),
                tickFirst = chunk.tickFirst,
                tickLast = chunk.tickLast,
                sourceKinds = MergeList(null, chunk.sourceKinds),
                fuzzySummary = OrcaMemoryWriter.BuildFuzzySummary(chunk.tags, 1),
                exemplarText = chunk.exemplarText,
                tags = MergeList(null, chunk.tags),
                keywords = MergeList(null, chunk.keywords),
                importance = chunk.importance,
                occurrenceCount = 1,
                centroidEmbedding = new List<float>(chunk.centroidEmbedding),
                createdAt = now,
                lastAccessed = now,
                embeddingState = "ready",
                memoryKind = "cluster",
                strength = chunk.strength,
                consolidationState = "active",
                representativeMemoryIds = new List<string> { chunk.id }
            };
        }

        private static void AddChunkToCluster(OrcaMemoryRecord cluster, OrcaMemoryRecord chunk)
        {
            int oldCount = Math.Max(1, cluster.occurrenceCount);
            cluster.centroidEmbedding = WeightedAverage(cluster.centroidEmbedding, oldCount, chunk.centroidEmbedding, 1);
            cluster.occurrenceCount = oldCount + 1;
            cluster.tickFirst = Math.Min(cluster.tickFirst, chunk.tickFirst);
            cluster.tickLast = Math.Max(cluster.tickLast, chunk.tickLast);
            cluster.importance = Math.Min(1f, Math.Max(cluster.importance, chunk.importance) + 0.02f);
            cluster.strength = Math.Min(1f, Math.Max(cluster.strength, chunk.strength) + 0.015f);
            cluster.lastAccessed = OrcaMemoryRecord.NowUnixSeconds();
            cluster.saveIds = MergeList(cluster.saveIds, chunk.saveIds);
            cluster.sourceKinds = MergeList(cluster.sourceKinds, chunk.sourceKinds);
            cluster.tags = MergeList(cluster.tags, chunk.tags);
            cluster.keywords = MergeList(cluster.keywords, chunk.keywords);
            cluster.fuzzySummary = OrcaMemoryWriter.BuildFuzzySummary(cluster.tags, cluster.occurrenceCount);
            if (cluster.representativeMemoryIds == null)
            {
                cluster.representativeMemoryIds = new List<string>();
            }
            if (cluster.representativeMemoryIds.Count < ClusterChunkSoftLimit && !cluster.representativeMemoryIds.Contains(chunk.id))
            {
                cluster.representativeMemoryIds.Add(chunk.id);
            }
        }

        public static OrcaMemoryRecord ClusterNeedingConsolidation(List<OrcaMemoryRecord> records)
        {
            List<OrcaMemoryRecord> clusters = records.Where(record => record != null && record.memoryKind == "cluster").ToList();
            OrcaMemoryRecord overfull = clusters
                .Select(cluster => new { cluster, count = ActiveChunksForCluster(records, cluster.id).Count })
                .Where(item => item.count > ClusterChunkHardConsolidationThreshold)
                .OrderByDescending(item => item.count)
                .Select(item => item.cluster)
                .FirstOrDefault();
            if (overfull != null)
            {
                return overfull;
            }

            int activeChunkVectorCount = records.Count(record => record != null && record.memoryKind == "chunk" && record.consolidationState == "active" && record.centroidEmbedding != null && record.centroidEmbedding.Count > 0);
            if (activeChunkVectorCount <= ChunkHardConsolidationThreshold)
            {
                return null;
            }

            return clusters
                .Select(cluster => new { cluster, count = ActiveChunksForCluster(records, cluster.id).Count })
                .Where(item => item.count > ClusterChunkSoftLimit)
                .OrderByDescending(item => item.count)
                .Select(item => item.cluster)
                .FirstOrDefault();
        }

        private static List<OrcaMemoryRecord> ActiveChunksForCluster(List<OrcaMemoryRecord> records, string clusterId)
        {
            return records.Where(record => record != null
                && record.memoryKind == "chunk"
                && record.clusterId == clusterId
                && record.consolidationState == "active").ToList();
        }

        public static bool ConsolidateCluster(List<OrcaMemoryRecord> records, OrcaMemoryRecord cluster)
        {
            List<OrcaMemoryRecord> chunks = ActiveChunksForCluster(records, cluster.id);
            if (chunks.Count <= ClusterChunkSoftLimit)
            {
                return false;
            }

            HashSet<string> keepIds = RepresentativeChunkIds(chunks);
            int compressed = 0;
            int deleted = 0;
            for (int i = chunks.Count - 1; i >= 0; i--)
            {
                OrcaMemoryRecord chunk = chunks[i];
                if (keepIds.Contains(chunk.id))
                {
                    continue;
                }

                if (ShouldCompressChunk(chunk))
                {
                    chunk.consolidationState = "compressed";
                    chunk.lastConsolidated = OrcaMemoryRecord.NowUnixSeconds();
                    compressed++;
                }
                else
                {
                    records.Remove(chunk);
                    deleted++;
                }
            }

            cluster.representativeMemoryIds = keepIds.ToList();
            cluster.lastConsolidated = OrcaMemoryRecord.NowUnixSeconds();
            cluster.fuzzySummary = OrcaMemoryWriter.BuildFuzzySummary(cluster.tags, cluster.occurrenceCount);
            OrcaMemoryRecord exemplar = chunks.Where(item => keepIds.Contains(item.id)).OrderByDescending(item => item.importance).FirstOrDefault();
            if (exemplar != null)
            {
                cluster.exemplarText = exemplar.exemplarText;
            }

            Debug("Memory sleep consolidation: cluster=" + cluster.id + " kept=" + keepIds.Count + " compressed=" + compressed + " deleted=" + deleted);
            return compressed > 0 || deleted > 0;
        }

        private static HashSet<string> RepresentativeChunkIds(List<OrcaMemoryRecord> chunks)
        {
            HashSet<string> ids = new HashSet<string>();
            AddRepresentatives(ids, chunks.OrderByDescending(record => record.importance).ThenByDescending(record => record.lastAccessed), 4);
            AddRepresentatives(ids, chunks.OrderByDescending(record => record.tickLast), 4);
            AddRepresentatives(ids, chunks.OrderByDescending(record => record.lastAccessed), 4);

            if (ids.Count > ClusterChunkSoftLimit)
            {
                ids = new HashSet<string>(chunks.Where(record => ids.Contains(record.id))
                    .OrderByDescending(record => record.importance)
                    .ThenByDescending(record => record.lastAccessed)
                    .Take(ClusterChunkSoftLimit)
                    .Select(record => record.id));
            }

            return ids;
        }

        private static void AddRepresentatives(HashSet<string> ids, IEnumerable<OrcaMemoryRecord> candidates, int count)
        {
            foreach (OrcaMemoryRecord record in candidates)
            {
                if (ids.Count >= ClusterChunkSoftLimit)
                {
                    return;
                }
                if (record != null && !record.id.NullOrEmpty())
                {
                    ids.Add(record.id);
                    count--;
                    if (count <= 0)
                    {
                        return;
                    }
                }
            }
        }

        public static bool ConsolidateGlobalOverflow(List<OrcaMemoryRecord> records)
        {
            int activeCount = records.Count(record => record != null && record.memoryKind == "chunk" && record.consolidationState == "active" && record.centroidEmbedding != null && record.centroidEmbedding.Count > 0);
            if (activeCount <= ChunkHardConsolidationThreshold)
            {
                return false;
            }

            bool changed = false;
            List<OrcaMemoryRecord> candidates = records
                .Where(record => record != null
                    && record.memoryKind == "chunk"
                    && record.consolidationState == "active"
                    && !record.clusterId.NullOrEmpty()
                    && record.centroidEmbedding != null
                    && record.centroidEmbedding.Count > 0)
                .OrderBy(record => ShouldCompressChunk(record) ? 1 : 0)
                .ThenBy(record => record.importance)
                .ThenBy(record => record.lastAccessed)
                .ToList();

            for (int i = 0; i < candidates.Count && activeCount > ChunkSoftLimit; i++)
            {
                OrcaMemoryRecord chunk = candidates[i];
                if (ShouldCompressChunk(chunk))
                {
                    chunk.consolidationState = "compressed";
                    chunk.lastConsolidated = OrcaMemoryRecord.NowUnixSeconds();
                }
                else
                {
                    records.Remove(chunk);
                }
                activeCount--;
                changed = true;
            }

            return changed;
        }

        public static void Trim(List<OrcaMemoryRecord> records)
        {
            while (records.Count(record => record != null && record.memoryKind == "cluster") > ClusterSoftLimit)
            {
                OrcaMemoryRecord lowest = records.Where(record => record != null && record.memoryKind == "cluster")
                    .OrderBy(record => record.importance)
                    .ThenBy(record => record.lastAccessed)
                    .FirstOrDefault();
                if (lowest == null)
                {
                    break;
                }
                records.Remove(lowest);
            }

            while (records.Count(record => record != null && record.memoryKind == "chunk" && record.consolidationState == "active") > ChunkHardTrimLimit)
            {
                OrcaMemoryRecord lowest = records.Where(record => record != null && record.memoryKind == "chunk" && record.consolidationState == "active")
                    .OrderBy(record => ShouldCompressChunk(record) ? 1 : 0)
                    .ThenBy(record => record.importance)
                    .ThenBy(record => record.lastAccessed)
                    .FirstOrDefault();
                if (lowest == null)
                {
                    break;
                }

                if (ShouldCompressChunk(lowest))
                {
                    lowest.consolidationState = "compressed";
                    lowest.lastConsolidated = OrcaMemoryRecord.NowUnixSeconds();
                }
                else
                {
                    records.Remove(lowest);
                }
            }
        }

        public static bool ShouldCompressChunk(OrcaMemoryRecord chunk)
        {
            if (chunk == null)
            {
                return false;
            }
            if (chunk.importance >= ImportantCompressionThreshold)
            {
                return true;
            }

            List<string> tags = chunk.tags ?? new List<string>();
            return tags.Contains("preference")
                || tags.Contains("promise")
                || tags.Contains("death")
                || tags.Contains("funeral")
                || tags.Contains("raid")
                || tags.Contains("betrayal")
                || tags.Contains("relationship");
        }

        public static List<float> WeightedAverage(List<float> a, int weightA, List<float> b, int weightB)
        {
            if (a == null || b == null || a.Count != b.Count || a.Count == 0)
            {
                return Normalize(b ?? new List<float>());
            }

            int total = Math.Max(1, weightA + weightB);
            List<float> result = new List<float>();
            for (int i = 0; i < a.Count; i++)
            {
                result.Add((a[i] * weightA + b[i] * weightB) / total);
            }

            return Normalize(result);
        }

        public static List<float> Normalize(List<float> values)
        {
            List<float> result = new List<float>();
            if (values == null || values.Count == 0)
            {
                return result;
            }

            double length = 0.0;
            for (int i = 0; i < values.Count; i++)
            {
                length += values[i] * values[i];
            }
            length = Math.Sqrt(length);
            if (length <= 0.0)
            {
                return new List<float>(values);
            }

            for (int i = 0; i < values.Count; i++)
            {
                result.Add((float)(values[i] / length));
            }

            return result;
        }

        private static List<string> MergeList(List<string> a, List<string> b)
        {
            List<string> result = new List<string>();
            if (a != null)
            {
                result.AddRange(a);
            }
            if (b != null)
            {
                result.AddRange(b);
            }

            return result.Where(value => !value.NullOrEmpty()).Distinct().Take(32).ToList();
        }

        private static string Clamp(string text, int maxChars)
        {
            if (text == null)
            {
                return "";
            }
            return text.Length <= maxChars ? text : text.Substring(0, maxChars) + "...";
        }

        private static void Debug(string message)
        {
            if (DeepseekTheOrcaMod.Settings != null && DeepseekTheOrcaMod.Settings.debugLogging)
            {
                Log.Message("[RimAgent] " + message);
            }
        }
    }
}
