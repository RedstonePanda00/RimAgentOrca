using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaLongTermMemoryService
    {
        private const int ChunkSoftLimit = 500;
        private const int ChunkHardConsolidationThreshold = 700;
        private const int ChunkHardTrimLimit = 900;
        private const int ClusterSoftLimit = 100;
        private const int ClusterChunkSoftLimit = 12;
        private const int ClusterChunkHardConsolidationThreshold = 20;
        private const int ConsolidationIntervalTicks = 2500;
        private const float ImportantCompressionThreshold = 0.75f;
        private const int MaxEmbeddingRetries = 3;
        private const int EmbeddingRetryBaseSeconds = 30;

        private static readonly object syncRoot = new object();
        private static readonly List<OrcaMemoryRecord> records = new List<OrcaMemoryRecord>();
        private static readonly List<OrcaRecentExperienceRecord> recentExperiences = new List<OrcaRecentExperienceRecord>();
        private static readonly OrcaMemoryKeywordIndex keywordIndex = new OrcaMemoryKeywordIndex();
        private static readonly OrcaEmbeddingClient embeddingClient = new OrcaEmbeddingClient();
        private static readonly LlmApiClient memoryClient = new LlmApiClient();
        private static bool loaded;
        private static string loadedPersonaKey = "";
        private static Task<OrcaEmbeddingResult> pendingEmbedding;
        private static OrcaMemoryRecord pendingEmbeddingRecord;
        private static Task<LlmChatResponse> pendingCompaction;
        private static List<string> pendingCompactionIds = new List<string>();
        private static int lastConsolidationTick = -ConsolidationIntervalTicks;

        public static string MemoryFolderPath
        {
            get { return Path.Combine(GenFilePaths.ConfigFolderPath, "DeepseekTheOrca", "Memory", CurrentPersonaStorageKey()); }
        }

        public static string MemoryFilePath
        {
            get { return ChunkMemoryFilePath; }
        }

        public static string RecentExperienceFilePath
        {
            get { return Path.Combine(MemoryFolderPath, "recent_experience.jsonl"); }
        }

        public static string ChunkMemoryFilePath
        {
            get { return Path.Combine(MemoryFolderPath, "memory_chunks.jsonl"); }
        }

        public static string ClusterMemoryFilePath
        {
            get { return Path.Combine(MemoryFolderPath, "memory_clusters.jsonl"); }
        }

        public static string KeywordIndexFilePath
        {
            get { return Path.Combine(MemoryFolderPath, "keyword_index.json"); }
        }

        public static void Add(string source, string text)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            text = (text ?? "").Trim();
            if (settings == null || !settings.enableLongTermMemory || text.NullOrEmpty())
            {
                return;
            }

            EnsureLoaded();
            OrcaRecentExperienceRecord record = OrcaRecentExperienceRecord.Create(source, Clamp(text.Replace("\r", " ").Replace("\n", " "), 1600));
            lock (syncRoot)
            {
                recentExperiences.Add(record);
                SaveRecentLocked();
            }

            Debug("Recent experience buffered: " + record.source + " chars=" + record.text.Length);
        }

        public static string ContextForPrompt(string query)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !settings.enableLongTermMemory)
            {
                return "";
            }

            EnsureLoaded();
            List<float> queryEmbedding = TryEmbedQueryForSemanticTopK(settings, query);
            List<OrcaMemoryRecord> selected;
            lock (syncRoot)
            {
                selected = OrcaMemoryRetriever.Retrieve(records, keywordIndex, query, queryEmbedding, settings.memoryMaxInjectedEntries);
                if (selected.Count > 0)
                {
                    long now = OrcaMemoryRecord.NowUnixSeconds();
                    foreach (OrcaMemoryRecord record in selected)
                    {
                        record.lastAccessed = now;
                    }
                    SaveRecordsLocked();
                }
            }

            if (selected.Count == 0)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Long-term memory. These memories belong only to the current persona. Fuzzy impressions are soft long-term context; memory chunks are compressed experience traces below current game data and tool results.");
            for (int i = 0; i < selected.Count; i++)
            {
                OrcaMemoryRecord record = selected[i];
                builder.Append(record.memoryKind == "cluster" ? "- [impression] " : "- [memory] ");
                builder.Append(record.DisplayText.Replace("\r", " ").Replace("\n", " "));
                if (record.memoryKind == "cluster" && record.occurrenceCount > 1)
                {
                    builder.Append(" [recurred ");
                    builder.Append(record.occurrenceCount);
                    builder.Append(" times]");
                }
                if (record.tags != null && record.tags.Count > 0)
                {
                    builder.Append(" [tags: ");
                    builder.Append(string.Join(", ", record.tags.Take(5).ToArray()));
                    builder.Append("]");
                }
                builder.AppendLine();
            }

            return builder.ToString();
        }

        public static void Tick()
        {
            EnsureLoaded();
            CompleteFinishedEmbedding();
            CompleteFinishedCompaction();

            if (TryStartOrRunCompactionIfIdle())
            {
                return;
            }

            if (TryConsolidateIfIdle())
            {
                return;
            }

            OrcaMemoryRecord record;
            lock (syncRoot)
            {
                if (pendingEmbedding != null)
                {
                    return;
                }

                record = records.FirstOrDefault(IsReadyForEmbeddingAttempt);
            }

            TryStartEmbedding(record);
        }

        public static List<OrcaMemoryRecord> AllRecords()
        {
            EnsureLoaded();
            lock (syncRoot)
            {
                return records.Where(record => record != null && record.consolidationState != "pruned")
                    .OrderBy(record => record.memoryKind == "cluster" ? 0 : 1)
                    .ThenByDescending(record => record.lastAccessed)
                    .ToList();
            }
        }

        public static List<OrcaRecentExperienceRecord> AllRecentExperiences()
        {
            EnsureLoaded();
            lock (syncRoot)
            {
                return recentExperiences.OrderByDescending(record => record.createdAt).ToList();
            }
        }

        public static void Delete(string id)
        {
            if (id.NullOrEmpty())
            {
                return;
            }

            EnsureLoaded();
            lock (syncRoot)
            {
                OrcaMemoryRecord record = records.FirstOrDefault(item => item != null && item.id == id);
                if (record != null && record.memoryKind == "cluster")
                {
                    for (int i = 0; i < records.Count; i++)
                    {
                        if (records[i] != null && records[i].clusterId == record.id)
                        {
                            records[i].clusterId = "";
                        }
                    }
                }
                records.RemoveAll(item => item != null && item.id == id);
                SaveAndRebuildIndexLocked();
            }
        }

        public static void Clear()
        {
            EnsureLoaded();
            lock (syncRoot)
            {
                records.Clear();
                recentExperiences.Clear();
                SaveRecentLocked();
                SaveAndRebuildIndexLocked();
            }
        }

        public static string CurrentPersonaId()
        {
            return DeepseekTheOrcaMod.Settings == null ? OrcaChatPersonaManager.BuiltInOrcaId : DeepseekTheOrcaMod.Settings.chatPersonaDefName ?? "";
        }

        public static string CurrentPersonaStorageKey()
        {
            string id = CurrentPersonaId();
            if (id.NullOrEmpty())
            {
                id = OrcaChatPersonaManager.BuiltInOrcaId;
            }

            StringBuilder builder = new StringBuilder(id.Length);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.')
                {
                    builder.Append(c);
                }
                else
                {
                    builder.Append('_');
                }
            }

            string key = builder.ToString().Trim('_');
            return key.NullOrEmpty() ? "unknown_persona" : key;
        }

        public static string CurrentSaveId()
        {
            try
            {
                if (Current.Game != null && Current.Game.Info != null && !Current.Game.Info.permadeathModeUniqueName.NullOrEmpty())
                {
                    return Current.Game.Info.permadeathModeUniqueName;
                }

                if (Find.World != null && Find.World.info != null)
                {
                    return (Find.World.info.name + "|" + Find.World.info.seedString).Trim('|');
                }
            }
            catch
            {
            }

            return "";
        }

        private static List<float> TryEmbedQueryForSemanticTopK(DeepseekTheOrcaSettings settings, string query)
        {
            if (settings == null || !settings.enableSemanticMemoryQuery || query.NullOrEmpty() || !settings.HasModelForRole(OrcaLlmModelRole.Embedding))
            {
                return null;
            }

            try
            {
                if (LlmRequestScheduler.IsBusy)
                {
                    return null;
                }

                int timeoutMs = Math.Min(settings.semanticMemoryQueryHardTimeoutMs, Math.Max(1000, settings.semanticMemoryQueryWaitMs + 250));
                Task<OrcaEmbeddingResult> task = embeddingClient.EmbedAsync(settings, query, timeoutMs);
                if (!task.Wait(settings.semanticMemoryQueryWaitMs))
                {
                    return null;
                }

                OrcaEmbeddingResult result = task.Result;
                return result != null && result.success ? Normalize(result.embedding) : null;
            }
            catch
            {
                return null;
            }
        }

        private static void CompleteFinishedEmbedding()
        {
            Task<OrcaEmbeddingResult> finished = null;
            OrcaMemoryRecord record = null;
            lock (syncRoot)
            {
                if (pendingEmbedding != null && pendingEmbedding.IsCompleted)
                {
                    finished = pendingEmbedding;
                    record = pendingEmbeddingRecord;
                    pendingEmbedding = null;
                    pendingEmbeddingRecord = null;
                }
            }

            if (finished != null && record != null)
            {
                CompleteEmbedding(finished, record);
            }
        }

        private static void TryStartEmbedding(OrcaMemoryRecord record)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (record == null || settings == null || !settings.HasModelForRole(OrcaLlmModelRole.Embedding))
            {
                return;
            }

            lock (syncRoot)
            {
                if (pendingEmbedding != null || !IsReadyForEmbeddingAttempt(record))
                {
                    return;
                }

                record.embeddingState = "embedding";
                pendingEmbeddingRecord = record;
                pendingEmbedding = embeddingClient.EmbedAsync(settings, record.DisplayText + "\n" + record.exemplarText);
                SaveAndRebuildIndexLocked();
            }
        }

        private static void CompleteEmbedding(Task<OrcaEmbeddingResult> finished, OrcaMemoryRecord record)
        {
            OrcaEmbeddingResult result;
            try
            {
                result = finished.Result;
            }
            catch (Exception ex)
            {
                MarkEmbeddingFailed(record);
                Debug("Embedding failed: " + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            if (result == null || !result.success || result.embedding == null || result.embedding.Count == 0)
            {
                MarkEmbeddingFailed(record);
                Debug("Embedding failed: " + (result == null ? "no response" : result.errorMessage));
                return;
            }

            List<float> normalized = Normalize(result.embedding);
            lock (syncRoot)
            {
                if (!records.Contains(record))
                {
                    return;
                }

                record.centroidEmbedding = normalized;
                record.embeddingState = "ready";
                record.embeddingRetryCount = 0;
                record.nextEmbeddingRetryAt = 0;
                if (record.memoryKind == "chunk")
                {
                    AttachChunkToClusterLocked(record);
                }

                TrimLocked();
                SaveAndRebuildIndexLocked();
            }
        }

        private static void MarkEmbeddingFailed(OrcaMemoryRecord record)
        {
            lock (syncRoot)
            {
                if (record != null && records.Contains(record))
                {
                    record.embeddingRetryCount++;
                    if (record.embeddingRetryCount >= MaxEmbeddingRetries)
                    {
                        record.embeddingState = "failed_permanent";
                        record.nextEmbeddingRetryAt = 0;
                    }
                    else
                    {
                        record.embeddingState = "failed_retryable";
                        int delaySeconds = EmbeddingRetryBaseSeconds * (int)Math.Pow(2, Math.Max(0, record.embeddingRetryCount - 1));
                        record.nextEmbeddingRetryAt = OrcaMemoryRecord.NowUnixSeconds() + delaySeconds;
                    }
                    SaveAndRebuildIndexLocked();
                }
            }
        }

        private static bool IsReadyForEmbeddingAttempt(OrcaMemoryRecord record)
        {
            if (record == null || record.memoryKind != "chunk" || record.consolidationState != "active")
            {
                return false;
            }
            if (record.embeddingState == "pending")
            {
                return true;
            }
            return record.embeddingState == "failed_retryable" && record.nextEmbeddingRetryAt <= OrcaMemoryRecord.NowUnixSeconds();
        }

        private static bool TryStartOrRunCompactionIfIdle()
        {
            if (!IsIdleForBackgroundWork())
            {
                return false;
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null)
            {
                return false;
            }

            List<OrcaRecentExperienceRecord> snapshot;
            lock (syncRoot)
            {
                if (pendingCompaction != null || recentExperiences.Count == 0)
                {
                    return false;
                }

                int estimatedTokens = EstimateRecentTokens(recentExperiences);
                if (estimatedTokens < settings.memoryCompactionTokenThreshold)
                {
                    return false;
                }

                snapshot = recentExperiences.OrderBy(record => record.createdAt).ToList();
            }

            if (!settings.HasModelForRole(OrcaLlmModelRole.Memory))
            {
                string localSummary = BuildLocalCompactionSummary(snapshot);
                AcceptCompactionSummary(localSummary, snapshot);
                Debug("Memory compaction used local fallback.");
                return true;
            }

            List<LlmChatMessage> messages = BuildCompactionMessages(snapshot);
            lock (syncRoot)
            {
                if (pendingCompaction != null)
                {
                    return false;
                }

                pendingCompactionIds = snapshot.Select(record => record.id).ToList();
                pendingCompaction = memoryClient.SendPlainChatCompletionAsync(settings, messages, OrcaLlmModelRole.Memory);
            }
            Debug("Memory compaction request sent to Memory model.");
            return true;
        }

        private static void CompleteFinishedCompaction()
        {
            Task<LlmChatResponse> finished = null;
            List<OrcaRecentExperienceRecord> compacted = null;
            lock (syncRoot)
            {
                if (pendingCompaction != null && pendingCompaction.IsCompleted)
                {
                    finished = pendingCompaction;
                    pendingCompaction = null;
                    compacted = recentExperiences.Where(record => pendingCompactionIds.Contains(record.id)).OrderBy(record => record.createdAt).ToList();
                    pendingCompactionIds = new List<string>();
                }
            }

            if (finished == null)
            {
                return;
            }

            LlmChatResponse response;
            try
            {
                response = finished.Result;
            }
            catch (Exception ex)
            {
                Debug("Memory compaction failed: " + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            if (response == null || !response.success || response.content.NullOrEmpty())
            {
                Debug("Memory compaction failed: " + (response == null ? "no response" : response.errorMessage));
                return;
            }

            AcceptCompactionSummary(response.content, compacted);
        }

        private static List<LlmChatMessage> BuildCompactionMessages(List<OrcaRecentExperienceRecord> experiences)
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

        private static string BuildLocalCompactionSummary(List<OrcaRecentExperienceRecord> experiences)
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

        private static void AcceptCompactionSummary(string summary, List<OrcaRecentExperienceRecord> compacted)
        {
            summary = (summary ?? "").Trim();
            if (summary.NullOrEmpty() || compacted == null || compacted.Count == 0)
            {
                return;
            }

            List<OrcaMemoryRecord> chunks = BuildChunks(summary, compacted);
            lock (syncRoot)
            {
                records.AddRange(chunks);
                HashSet<string> ids = new HashSet<string>(compacted.Select(record => record.id));
                recentExperiences.RemoveAll(record => ids.Contains(record.id));
                TrimLocked();
                SaveRecentLocked();
                SaveAndRebuildIndexLocked();
            }

            Debug("Memory compaction accepted: chunks=" + chunks.Count + " clearedRecent=" + compacted.Count);
        }

        private static List<OrcaMemoryRecord> BuildChunks(string summary, List<OrcaRecentExperienceRecord> compacted)
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

        private static void AttachChunkToClusterLocked(OrcaMemoryRecord chunk)
        {
            OrcaMemoryRecord cluster = BestClusterTarget(chunk, chunk.centroidEmbedding);
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

        private static OrcaMemoryRecord BestClusterTarget(OrcaMemoryRecord source, List<float> sourceEmbedding)
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

        private static bool TryConsolidateIfIdle()
        {
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            if (tick - lastConsolidationTick < ConsolidationIntervalTicks || !IsIdleForBackgroundWork())
            {
                return false;
            }

            lock (syncRoot)
            {
                lastConsolidationTick = tick;
                OrcaMemoryRecord cluster = ClusterNeedingConsolidationLocked();
                bool changed = cluster == null ? ConsolidateGlobalOverflowLocked() : ConsolidateClusterLocked(cluster);
                TrimLocked();
                if (changed)
                {
                    SaveAndRebuildIndexLocked();
                }
                return changed;
            }
        }

        private static bool IsIdleForBackgroundWork()
        {
            lock (syncRoot)
            {
                if (pendingEmbedding != null || pendingCompaction != null)
                {
                    return false;
                }
            }

            try
            {
                return OrcaChatWindowManager.Session == null || !OrcaChatWindowManager.Session.IsWaiting;
            }
            catch
            {
                return true;
            }
        }

        private static OrcaMemoryRecord ClusterNeedingConsolidationLocked()
        {
            List<OrcaMemoryRecord> clusters = records.Where(record => record != null && record.memoryKind == "cluster").ToList();
            OrcaMemoryRecord overfull = clusters
                .Select(cluster => new { cluster, count = ActiveChunksForCluster(cluster.id).Count })
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
                .Select(cluster => new { cluster, count = ActiveChunksForCluster(cluster.id).Count })
                .Where(item => item.count > ClusterChunkSoftLimit)
                .OrderByDescending(item => item.count)
                .Select(item => item.cluster)
                .FirstOrDefault();
        }

        private static List<OrcaMemoryRecord> ActiveChunksForCluster(string clusterId)
        {
            return records.Where(record => record != null
                && record.memoryKind == "chunk"
                && record.clusterId == clusterId
                && record.consolidationState == "active").ToList();
        }

        private static bool ConsolidateClusterLocked(OrcaMemoryRecord cluster)
        {
            List<OrcaMemoryRecord> chunks = ActiveChunksForCluster(cluster.id);
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

        private static bool ConsolidateGlobalOverflowLocked()
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

        private static bool ShouldCompressChunk(OrcaMemoryRecord chunk)
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

        private static int EstimateRecentTokens(List<OrcaRecentExperienceRecord> experiences)
        {
            int tokens = 0;
            for (int i = 0; i < experiences.Count; i++)
            {
                tokens += experiences[i] == null || experiences[i].text == null ? 0 : OrcaTokenEstimator.Estimate(experiences[i].text);
            }
            return tokens;
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

        private static List<float> WeightedAverage(List<float> a, int weightA, List<float> b, int weightB)
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

        private static List<float> Normalize(List<float> values)
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

        private static void EnsureLoaded()
        {
            string personaKey = CurrentPersonaStorageKey();
            if (loaded && loadedPersonaKey == personaKey)
            {
                return;
            }

            lock (syncRoot)
            {
                if (loaded && loadedPersonaKey == personaKey)
                {
                    return;
                }

                loaded = true;
                loadedPersonaKey = personaKey;
                pendingEmbedding = null;
                pendingEmbeddingRecord = null;
                pendingCompaction = null;
                pendingCompactionIds = new List<string>();
                records.Clear();
                recentExperiences.Clear();
                Directory.CreateDirectory(MemoryFolderPath);
                LoadRecent();
                LoadRecords(ChunkMemoryFilePath, "chunk");
                LoadRecords(ClusterMemoryFilePath, "cluster");

                TrimLocked();
                if (!keywordIndex.TryLoad(KeywordIndexFilePath, records))
                {
                    keywordIndex.Rebuild(records);
                    keywordIndex.Save(KeywordIndexFilePath);
                }
            }
        }

        private static void LoadRecent()
        {
            if (!File.Exists(RecentExperienceFilePath))
            {
                return;
            }

            foreach (string line in File.ReadAllLines(RecentExperienceFilePath))
            {
                OrcaRecentExperienceRecord record = RecentFromJson(line);
                if (record != null && !record.id.NullOrEmpty() && !record.text.NullOrEmpty())
                {
                    recentExperiences.Add(record);
                }
            }
        }

        private static void LoadRecords(string filePath, string defaultKind)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            foreach (string line in File.ReadAllLines(filePath))
            {
                OrcaMemoryRecord record = FromJson(line);
                if (record != null && !record.id.NullOrEmpty())
                {
                    if (record.memoryKind.NullOrEmpty())
                    {
                        record.memoryKind = defaultKind;
                    }
                    records.Add(record);
                }
            }
        }

        private static void TrimLocked()
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

        private static void SaveAndRebuildIndexLocked()
        {
            SaveRecordsLocked();
            keywordIndex.Rebuild(records);
            keywordIndex.Save(KeywordIndexFilePath);
        }

        private static void SaveRecordsLocked()
        {
            Directory.CreateDirectory(MemoryFolderPath);
            SaveRecords(ChunkMemoryFilePath, records.Where(record => record != null && record.memoryKind == "chunk" && record.consolidationState != "pruned"));
            SaveRecords(ClusterMemoryFilePath, records.Where(record => record != null && record.memoryKind == "cluster" && record.consolidationState != "pruned"));
        }

        private static void SaveRecentLocked()
        {
            Directory.CreateDirectory(MemoryFolderPath);
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < recentExperiences.Count; i++)
            {
                builder.AppendLine(RecentToJson(recentExperiences[i]));
            }
            File.WriteAllText(RecentExperienceFilePath, builder.ToString());
        }

        private static void SaveRecords(string filePath, IEnumerable<OrcaMemoryRecord> source)
        {
            StringBuilder builder = new StringBuilder();
            foreach (OrcaMemoryRecord record in source)
            {
                builder.AppendLine(ToJson(record));
            }

            File.WriteAllText(filePath, builder.ToString());
        }

        private static string RecentToJson(OrcaRecentExperienceRecord record)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["id"] = record.id;
            root["personaId"] = record.personaId;
            root["saveId"] = record.saveId;
            root["source"] = record.source;
            root["text"] = record.text;
            root["tick"] = record.tick;
            root["createdAt"] = record.createdAt;
            return MiniJson.Serialize(root);
        }

        private static OrcaRecentExperienceRecord RecentFromJson(string json)
        {
            try
            {
                Dictionary<string, object> root = MiniJson.Deserialize(json) as Dictionary<string, object>;
                if (root == null)
                {
                    return null;
                }

                return new OrcaRecentExperienceRecord
                {
                    id = GetString(root, "id"),
                    personaId = GetString(root, "personaId"),
                    saveId = GetString(root, "saveId"),
                    source = GetString(root, "source"),
                    text = GetString(root, "text"),
                    tick = GetInt(root, "tick"),
                    createdAt = GetLong(root, "createdAt")
                };
            }
            catch
            {
                return null;
            }
        }

        private static string ToJson(OrcaMemoryRecord record)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["id"] = record.id;
            root["personaId"] = record.personaId;
            root["saveIds"] = record.saveIds ?? new List<string>();
            root["tickFirst"] = record.tickFirst;
            root["tickLast"] = record.tickLast;
            root["sourceKinds"] = record.sourceKinds ?? new List<string>();
            root["fuzzySummary"] = record.fuzzySummary;
            root["exemplarText"] = record.exemplarText;
            root["tags"] = record.tags ?? new List<string>();
            root["keywords"] = record.keywords ?? new List<string>();
            root["importance"] = record.importance;
            root["occurrenceCount"] = record.occurrenceCount;
            root["centroidEmbedding"] = record.centroidEmbedding ?? new List<float>();
            root["createdAt"] = record.createdAt;
            root["lastAccessed"] = record.lastAccessed;
            root["embeddingState"] = record.embeddingState ?? "pending";
            root["memoryKind"] = record.memoryKind ?? "chunk";
            root["clusterId"] = record.clusterId ?? "";
            root["sourceRange"] = record.sourceRange ?? "";
            root["strength"] = record.strength;
            root["consolidationState"] = record.consolidationState ?? "active";
            root["lastConsolidated"] = record.lastConsolidated;
            root["representativeMemoryIds"] = record.representativeMemoryIds ?? new List<string>();
            root["embeddingRetryCount"] = record.embeddingRetryCount;
            root["nextEmbeddingRetryAt"] = record.nextEmbeddingRetryAt;
            return MiniJson.Serialize(root);
        }

        private static OrcaMemoryRecord FromJson(string json)
        {
            try
            {
                Dictionary<string, object> root = MiniJson.Deserialize(json) as Dictionary<string, object>;
                if (root == null)
                {
                    return null;
                }

                OrcaMemoryRecord record = new OrcaMemoryRecord();
                record.id = GetString(root, "id");
                record.personaId = GetString(root, "personaId");
                record.saveIds = GetStringList(root, "saveIds");
                record.tickFirst = GetInt(root, "tickFirst");
                record.tickLast = GetInt(root, "tickLast");
                record.sourceKinds = GetStringList(root, "sourceKinds");
                record.fuzzySummary = GetString(root, "fuzzySummary");
                record.exemplarText = GetString(root, "exemplarText");
                record.tags = GetStringList(root, "tags");
                record.keywords = GetStringList(root, "keywords");
                record.importance = GetFloat(root, "importance");
                record.occurrenceCount = Math.Max(1, GetInt(root, "occurrenceCount"));
                record.centroidEmbedding = GetFloatList(root, "centroidEmbedding");
                record.createdAt = GetLong(root, "createdAt");
                record.lastAccessed = GetLong(root, "lastAccessed");
                record.embeddingState = GetString(root, "embeddingState");
                record.memoryKind = GetString(root, "memoryKind");
                record.clusterId = GetString(root, "clusterId");
                record.sourceRange = GetString(root, "sourceRange");
                record.strength = GetFloat(root, "strength");
                record.consolidationState = GetString(root, "consolidationState");
                record.lastConsolidated = GetLong(root, "lastConsolidated");
                record.representativeMemoryIds = GetStringList(root, "representativeMemoryIds");
                record.embeddingRetryCount = GetInt(root, "embeddingRetryCount");
                record.nextEmbeddingRetryAt = GetLong(root, "nextEmbeddingRetryAt");
                if (record.embeddingState.NullOrEmpty())
                {
                    record.embeddingState = record.centroidEmbedding.Count > 0 ? "ready" : "pending";
                }
                if (record.memoryKind.NullOrEmpty())
                {
                    record.memoryKind = record.fuzzySummary.NullOrEmpty() ? "chunk" : "cluster";
                }
                if (record.consolidationState.NullOrEmpty())
                {
                    record.consolidationState = "active";
                }
                if (record.strength <= 0f)
                {
                    record.strength = record.importance;
                }
                return record;
            }
            catch
            {
                return null;
            }
        }

        private static string GetString(Dictionary<string, object> root, string key)
        {
            object value;
            return root.TryGetValue(key, out value) && value != null ? value.ToString() : "";
        }

        private static int GetInt(Dictionary<string, object> root, string key)
        {
            object value;
            if (!root.TryGetValue(key, out value) || value == null)
            {
                return 0;
            }
            if (value is long)
            {
                return (int)(long)value;
            }
            if (value is double)
            {
                return (int)(double)value;
            }
            int parsed;
            return int.TryParse(value.ToString(), out parsed) ? parsed : 0;
        }

        private static long GetLong(Dictionary<string, object> root, string key)
        {
            object value;
            if (!root.TryGetValue(key, out value) || value == null)
            {
                return 0L;
            }
            if (value is long)
            {
                return (long)value;
            }
            if (value is double)
            {
                return (long)(double)value;
            }
            long parsed;
            return long.TryParse(value.ToString(), out parsed) ? parsed : 0L;
        }

        private static float GetFloat(Dictionary<string, object> root, string key)
        {
            object value;
            if (!root.TryGetValue(key, out value) || value == null)
            {
                return 0f;
            }
            if (value is double)
            {
                return (float)(double)value;
            }
            float parsed;
            return float.TryParse(value.ToString(), out parsed) ? parsed : 0f;
        }

        private static List<string> GetStringList(Dictionary<string, object> root, string key)
        {
            object value;
            List<object> raw = root.TryGetValue(key, out value) ? value as List<object> : null;
            return raw == null ? new List<string>() : raw.Where(item => item != null).Select(item => item.ToString()).Where(item => !item.NullOrEmpty()).Distinct().ToList();
        }

        private static List<float> GetFloatList(Dictionary<string, object> root, string key)
        {
            object value;
            List<object> raw = root.TryGetValue(key, out value) ? value as List<object> : null;
            List<float> result = new List<float>();
            if (raw == null)
            {
                return result;
            }

            for (int i = 0; i < raw.Count; i++)
            {
                object item = raw[i];
                if (item is double)
                {
                    result.Add((float)(double)item);
                    continue;
                }
                float parsed;
                if (item != null && float.TryParse(item.ToString(), out parsed))
                {
                    result.Add(parsed);
                }
            }
            return result;
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
