using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace DeepseekTheOrca
{
    // Facade for long-term memory: owns the record state, the keyword index,
    // and the embedding/compaction task lifecycle. Persistence lives in
    // OrcaMemoryStore and consolidation policy in OrcaMemoryCompactor.
    public static class OrcaLongTermMemoryService
    {
        private const int ConsolidationIntervalTicks = 2500;
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
                return result != null && result.success ? OrcaMemoryCompactor.Normalize(result.embedding) : null;
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

            List<float> normalized = OrcaMemoryCompactor.Normalize(result.embedding);
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
                    OrcaMemoryCompactor.AttachChunkToCluster(records, record);
                }

                OrcaMemoryCompactor.Trim(records);
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

                int estimatedTokens = OrcaMemoryCompactor.EstimateRecentTokens(recentExperiences);
                if (estimatedTokens < settings.memoryCompactionTokenThreshold)
                {
                    return false;
                }

                snapshot = recentExperiences.OrderBy(record => record.createdAt).ToList();
            }

            if (!settings.HasModelForRole(OrcaLlmModelRole.Memory))
            {
                string localSummary = OrcaMemoryCompactor.BuildLocalCompactionSummary(snapshot);
                AcceptCompactionSummary(localSummary, snapshot);
                Debug("Memory compaction used local fallback.");
                return true;
            }

            List<LlmChatMessage> messages = OrcaMemoryCompactor.BuildCompactionMessages(snapshot);
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

        private static void AcceptCompactionSummary(string summary, List<OrcaRecentExperienceRecord> compacted)
        {
            summary = (summary ?? "").Trim();
            if (summary.NullOrEmpty() || compacted == null || compacted.Count == 0)
            {
                return;
            }

            List<OrcaMemoryRecord> chunks = OrcaMemoryCompactor.BuildChunks(summary, compacted);
            lock (syncRoot)
            {
                records.AddRange(chunks);
                HashSet<string> ids = new HashSet<string>(compacted.Select(record => record.id));
                recentExperiences.RemoveAll(record => ids.Contains(record.id));
                OrcaMemoryCompactor.Trim(records);
                SaveRecentLocked();
                SaveAndRebuildIndexLocked();
            }

            Debug("Memory compaction accepted: chunks=" + chunks.Count + " clearedRecent=" + compacted.Count);
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
                OrcaMemoryRecord cluster = OrcaMemoryCompactor.ClusterNeedingConsolidation(records);
                bool changed = cluster == null
                    ? OrcaMemoryCompactor.ConsolidateGlobalOverflow(records)
                    : OrcaMemoryCompactor.ConsolidateCluster(records, cluster);
                OrcaMemoryCompactor.Trim(records);
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
                return !OrcaChatAgentHub.IsChatBusy;
            }
            catch
            {
                return true;
            }
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
                recentExperiences.AddRange(OrcaMemoryStore.LoadRecent(RecentExperienceFilePath));
                records.AddRange(OrcaMemoryStore.LoadRecords(ChunkMemoryFilePath, "chunk"));
                records.AddRange(OrcaMemoryStore.LoadRecords(ClusterMemoryFilePath, "cluster"));

                OrcaMemoryCompactor.Trim(records);
                if (!keywordIndex.TryLoad(KeywordIndexFilePath, records))
                {
                    keywordIndex.Rebuild(records);
                    keywordIndex.Save(KeywordIndexFilePath);
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
            OrcaMemoryStore.SaveRecords(ChunkMemoryFilePath, records.Where(record => record != null && record.memoryKind == "chunk" && record.consolidationState != "pruned"));
            OrcaMemoryStore.SaveRecords(ClusterMemoryFilePath, records.Where(record => record != null && record.memoryKind == "cluster" && record.consolidationState != "pruned"));
        }

        private static void SaveRecentLocked()
        {
            Directory.CreateDirectory(MemoryFolderPath);
            OrcaMemoryStore.SaveRecent(RecentExperienceFilePath, recentExperiences);
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
