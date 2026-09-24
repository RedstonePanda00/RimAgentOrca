using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace DeepseekTheOrca
{
    // Facade for long-term memory: owns the record state, the keyword index,
    // and the embedding/compaction task lifecycle. Persistence lives in
    // OrcaMemoryStore and consolidation policy in OrcaMemoryCompactor.
    public static partial class OrcaLongTermMemoryService
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
        private static string pendingEmbeddingIdentity;
        private static Task<LlmChatResponse> pendingCompaction;
        private static bool embeddingQueueCancelled;
        private static string reconciledEmbeddingIdentity = "";
        private static long nextIdentityCheck;
        private static HashSet<string> pendingCompactionIds = new HashSet<string>();
        private static CancellationTokenSource compactionCancellation;
        private static OrcaMemoryCompactionState compactionState = new OrcaMemoryCompactionState();
        private static long recentEstimatedTokens;
        private static List<OrcaMemoryRecord> pendingCommitChunks;
        private static List<string> pendingCommitIds;
        private static bool accessMetadataDirty;
        private static long nextAccessMetadataFlush;
        private static int lastConsolidationTick = -ConsolidationIntervalTicks;

        public static string MemoryFolderPath
        {
            get { return Path.Combine(configFolder(), "DeepseekTheOrca", "Memory", CurrentPersonaStorageKey()); }
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

        public static void Add(string source, string text) { RunStorage(() => AddCore(source, text)); }

        private static void AddCore(string source, string text)
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
                recentEstimatedTokens += OrcaTokenEstimator.Estimate(record.text);
                SaveRecentLocked();
            }

            Debug("Recent experience buffered: " + record.source + " chars=" + record.text.Length);
        }

        public static string ContextForPrompt(string query) { return RunStorage(() => ContextForPromptCore(query), ""); }

        private static string ContextForPromptCore(string query)
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
                    accessMetadataDirty = true;
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

        public static void Tick() { RunStorage(() => TickCore()); }

        private static void TickCore()
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || (!settings.enableLongTermMemory && !loaded)) return;
            EnsureLoaded();
            if (!settings.enableLongTermMemory && compactionCancellation != null)
                compactionCancellation.Cancel();
            if (!settings.enableLongTermMemory && pendingEmbedding != null && !embeddingQueueCancelled)
            {
                embeddingClient.CancelQueuedRequests();
                embeddingQueueCancelled = true;
            }
            if (settings.enableLongTermMemory || pendingEmbedding == null) embeddingQueueCancelled = false;
            CompleteFinishedEmbedding();
            CompleteFinishedCompaction();
            if (accessMetadataDirty && OrcaMemoryRecord.NowUnixSeconds() >= nextAccessMetadataFlush) FlushAccessMetadata();

            // Results already in flight may be committed, but disabled memory must never
            // start another request or local compaction/consolidation job.
            if (!settings.enableLongTermMemory) return;

            long now = OrcaMemoryRecord.NowUnixSeconds();
            if (now >= nextIdentityCheck)
            {
                nextIdentityCheck = now + 1;
                string identity = OrcaEmbeddingIdentity.Current;
                if (identity.Length > 0 && identity != reconciledEmbeddingIdentity)
                {
                    lock (syncRoot)
                    {
                        if (InvalidateEmbeddings(records, identity)) SaveAndRebuildIndexLocked();
                        reconciledEmbeddingIdentity = identity;
                    }
                }
            }

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

        public static List<OrcaMemoryRecord> AllRecords() { return RunStorage(() => AllRecordsCore(), new List<OrcaMemoryRecord>()); }

        private static List<OrcaMemoryRecord> AllRecordsCore()
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

        public static List<OrcaRecentExperienceRecord> AllRecentExperiences() { return RunStorage(() => AllRecentExperiencesCore(), new List<OrcaRecentExperienceRecord>()); }

        private static List<OrcaRecentExperienceRecord> AllRecentExperiencesCore()
        {
            EnsureLoaded();
            lock (syncRoot)
            {
                return recentExperiences.OrderByDescending(record => record.createdAt).ToList();
            }
        }

        public static void Delete(string id) { RunStorage(() => DeleteCore(id)); }

        private static void DeleteCore(string id)
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

        public static void Clear() { RunStorage(() => ClearCore()); }

        private static void ClearCore()
        {
            EnsureLoaded();
            lock (syncRoot)
            {
                // Delete the recovery intent before altering live data; if locked,
                // leave the current memory intact and let the player retry the clear.
                if (File.Exists(StorageJournalPath)) File.Delete(StorageJournalPath);
                records.Clear();
                recentExperiences.Clear();
                recentEstimatedTokens = 0;
                embeddingClient.CancelQueuedRequests();
                pendingEmbedding = null;
                pendingEmbeddingRecord = null;
                if (compactionCancellation != null) { compactionCancellation.Cancel(); compactionCancellation.Dispose(); compactionCancellation = null; }
                pendingCompaction = null;
                pendingCompactionIds.Clear();
                compactionState.Reset();
                pendingCommitChunks = null;
                pendingCommitIds = null;
                SaveCompactionStateLocked();
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
            return settings != null && settings.enableLongTermMemory && settings.enableSemanticMemoryQuery ? OrcaSemanticQueryCache.Ready(query) : null;
        }

        private static void CompleteFinishedEmbedding()
        {
            Task<OrcaEmbeddingResult> finished = null;
            OrcaMemoryRecord record = null;
            string identity = null;
            lock (syncRoot)
            {
                if (pendingEmbedding != null && pendingEmbedding.IsCompleted)
                {
                    finished = pendingEmbedding;
                    record = pendingEmbeddingRecord;
                    identity = pendingEmbeddingIdentity;
                    pendingEmbedding = null;
                    pendingEmbeddingRecord = null;
                }
            }

            if (finished != null && record != null)
            {
                if (finished.IsCanceled || !OrcaEmbeddingIdentity.Matches(identity, OrcaEmbeddingIdentity.Current))
                {
                    record.embeddingState = "pending";
                    SaveAndRebuildIndexLocked();
                    return;
                }
                CompleteEmbedding(finished, record);
            }
        }

        private static void TryStartEmbedding(OrcaMemoryRecord record)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (record == null || settings == null || !settings.enableLongTermMemory || !settings.HasModelForRole(OrcaLlmModelRole.Embedding))
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
                pendingEmbeddingIdentity = OrcaEmbeddingIdentity.Current;
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

                if (!OrcaEmbeddingIdentity.Matches(result.identity, OrcaEmbeddingIdentity.Current))
                {
                    record.embeddingState = "pending";
                    SaveAndRebuildIndexLocked();
                    return;
                }

                record.centroidEmbedding = normalized;
                record.embeddingIdentity = result.identity;
                record.embeddingState = "ready";
                record.embeddingRetryCount = 0;
                record.nextEmbeddingRetryAt = 0;
                if (record.memoryKind == "chunk" && string.IsNullOrEmpty(record.clusterId))
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
            if (record == null || (record.memoryKind != "chunk" && record.memoryKind != "cluster" && record.memoryKind != "atomic") || record.consolidationState != "active")
            {
                return false;
            }
            if (record.embeddingState == "pending")
            {
                return true;
            }
            return record.embeddingState == "failed_retryable" && record.nextEmbeddingRetryAt <= OrcaMemoryRecord.NowUnixSeconds();
        }

        internal static bool InvalidateEmbeddings(IEnumerable<OrcaMemoryRecord> source, string identity)
        {
            if (string.IsNullOrEmpty(identity)) return false;
            bool changed = false;
            foreach (var record in source)
            {
                if (record == null || record.consolidationState != "active" || OrcaEmbeddingIdentity.Matches(record.embeddingIdentity, identity)) continue;
                // Keep text, IDs and cluster membership. Only derived vectors are replaced.
                record.centroidEmbedding.Clear();
                record.embeddingIdentity = identity;
                record.embeddingState = "pending";
                record.embeddingRetryCount = 0;
                record.nextEmbeddingRetryAt = 0;
                changed = true;
            }
            return changed;
        }

        private static bool TryStartOrRunCompactionIfIdle()
        {
            if (!IsIdleForBackgroundWork())
            {
                return false;
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !settings.enableLongTermMemory)
            {
                return false;
            }

            if (compactionState.CanStart(OrcaMemoryRecord.NowUnixSeconds())
                && pendingCommitChunks != null)
            {
                CompleteCompactionCommit();
                return true;
            }

            List<OrcaRecentExperienceRecord> snapshot;
            lock (syncRoot)
            {
                if (pendingCompaction != null || recentExperiences.Count == 0 || !compactionState.CanStart(OrcaMemoryRecord.NowUnixSeconds()))
                {
                    return false;
                }

                if (recentEstimatedTokens < settings.memoryCompactionTokenThreshold)
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

                pendingCompactionIds = new HashSet<string>(snapshot.Select(record => record.id));
                compactionState.Begin(OrcaMemoryRecord.NowUnixSeconds());
                SaveCompactionStateLocked();
                compactionCancellation = new CancellationTokenSource();
                // Retry policy belongs to this workflow. Each attempt sends exactly once
                // and waits behind foreground chat/planning in the shared scheduler.
                pendingCompaction = memoryClient.SendBackgroundCompletionAsync(settings.RequestConfigForRole(OrcaLlmModelRole.Memory),
                    messages, 800, compactionCancellation.Token, OrcaLlmModelRole.Memory, 0.85f);
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
                    pendingCompactionIds.Clear();
                    if (compactionCancellation != null) { compactionCancellation.Dispose(); compactionCancellation = null; }
                }
            }

            if (finished == null)
            {
                return;
            }

            if (finished.IsCanceled)
            {
                compactionState.CancelQueued();
                SaveCompactionStateLocked();
                return;
            }

            LlmChatResponse response;
            try
            {
                response = finished.Result;
            }
            catch (Exception ex)
            {
                FailCompaction(ex.GetType().Name + ": " + ex.Message);
                return;
            }

            if (response == null || !response.success || response.content.NullOrEmpty()
                || response.finishReason == "length" || response.finishReason == "content_filter" || response.toolCalls.Count > 0)
            {
                FailCompaction(response == null ? "no response" : response.success ? "Incomplete compaction response: " + response.finishReason : response.errorMessage);
                return;
            }

            AcceptCompactionSummary(response.content, compacted);
        }

        private static void FailCompaction(string error)
        {
            compactionState.Fail(OrcaMemoryRecord.NowUnixSeconds(), error);
            SaveCompactionStateLocked();
            Debug("Memory compaction failed: " + error);
        }

        private static void SaveCompactionStateLocked()
        {
            OrcaMemoryStore.SaveCompactionState(StorageStatePath, compactionState);
        }

        private static void AcceptCompactionSummary(string summary, List<OrcaRecentExperienceRecord> compacted)
        {
            summary = (summary ?? "").Trim();
            if (summary.NullOrEmpty() || compacted == null || compacted.Count == 0)
            {
                return;
            }

            pendingCommitChunks = OrcaMemoryCompactor.BuildChunks(summary, compacted);
            pendingCommitIds = compacted.Select(record => record.id).ToList();
            CompleteCompactionCommit();
        }

        private static void CompleteCompactionCommit()
        {
            lock (syncRoot)
            {
                if (pendingCommitChunks == null) return;
                if (!File.Exists(StorageJournalPath))
                    OrcaMemoryStore.PrepareCompactionCommit(StorageJournalPath, pendingCommitChunks, pendingCommitIds);
                // A partial write is replayable. Do not discard the generated result on failure.
                SaveRecordsLocked();
                SaveRecentLocked();
                OrcaMemoryStore.RecoverCompactionCommit(StorageJournalPath, StorageChunkPath, StorageRecentPath);
                var existingIds = new HashSet<string>(records.Select(record => record.id));
                foreach (var chunk in pendingCommitChunks)
                    if (existingIds.Add(chunk.id)) records.Add(chunk);
                var consumed = new HashSet<string>(pendingCommitIds);
                recentExperiences.RemoveAll(record => consumed.Contains(record.id));
                recentEstimatedTokens = recentExperiences.Sum(record => (long)OrcaTokenEstimator.Estimate(record.text));
                pendingCommitChunks = null;
                pendingCommitIds = null;
                compactionState.Reset();
                keywordIndex.Rebuild(records);
                keywordIndex.Save(StorageIndexPath);
                SaveCompactionStateLocked();
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

                // Never abandon a successfully generated summary during a persona switch.
                if (loaded && pendingCommitChunks != null) CompleteCompactionCommit();
                if (accessMetadataDirty) FlushAccessMetadata();
                loaded = false;
                loadedPersonaKey = personaKey;
                reconciledEmbeddingIdentity = "";
                nextIdentityCheck = 0;
                embeddingClient.CancelQueuedRequests();
                pendingEmbedding = null;
                pendingEmbeddingRecord = null;
                if (compactionCancellation != null) { compactionCancellation.Cancel(); compactionCancellation.Dispose(); compactionCancellation = null; }
                pendingCompaction = null;
                pendingCompactionIds.Clear();
                pendingCommitChunks = null;
                pendingCommitIds = null;
                records.Clear();
                recentExperiences.Clear();
                Directory.CreateDirectory(StorageFolderPath);
                bool recoveredCommit = OrcaMemoryStore.RecoverCompactionCommit(StorageJournalPath, StorageChunkPath, StorageRecentPath);
                var loadedRecent = OrcaMemoryStore.LoadRecent(StorageRecentPath);
                var loadedChunks = OrcaMemoryStore.LoadRecords(StorageChunkPath, "chunk");
                var loadedClusters = OrcaMemoryStore.LoadRecords(StorageClusterPath, "cluster");
                var loadedCompaction = OrcaMemoryStore.LoadCompactionState(StorageStatePath);
                if (recoveredCommit) loadedCompaction.Reset();
                recentExperiences.AddRange(loadedRecent);
                records.AddRange(loadedChunks);
                records.AddRange(loadedClusters);
                recentEstimatedTokens = recentExperiences.Sum(record => (long)OrcaTokenEstimator.Estimate(record.text));
                compactionState = loadedCompaction;
                SaveCompactionStateLocked();

                OrcaMemoryCompactor.Trim(records);
                if (!keywordIndex.TryLoad(StorageIndexPath, records))
                {
                    keywordIndex.Rebuild(records);
                    keywordIndex.Save(StorageIndexPath);
                }
                loaded = true;
            }
        }

        private static void SaveAndRebuildIndexLocked()
        {
            SaveRecordsLocked();
            keywordIndex.Rebuild(records);
            keywordIndex.Save(StorageIndexPath);
        }

        private static void SaveRecordsLocked()
        {
            Directory.CreateDirectory(StorageFolderPath);
            OrcaMemoryStore.SaveRecords(StorageChunkPath, records.Where(record => record != null && record.memoryKind == "chunk" && record.consolidationState != "pruned"));
            OrcaMemoryStore.SaveRecords(StorageClusterPath, records.Where(record => record != null && record.memoryKind == "cluster" && record.consolidationState != "pruned"));
            accessMetadataDirty = false;
            nextAccessMetadataFlush = OrcaMemoryRecord.NowUnixSeconds() + 30;
        }

        private static void FlushAccessMetadata()
        {
            if (!loaded || !accessMetadataDirty) return;
            string folder = StorageFolderPath;
            OrcaMemoryStore.SaveRecords(Path.Combine(folder, "memory_chunks.jsonl"), records.Where(record => record != null && record.memoryKind == "chunk" && record.consolidationState != "pruned"));
            OrcaMemoryStore.SaveRecords(Path.Combine(folder, "memory_clusters.jsonl"), records.Where(record => record != null && record.memoryKind == "cluster" && record.consolidationState != "pruned"));
            accessMetadataDirty = false;
            nextAccessMetadataFlush = OrcaMemoryRecord.NowUnixSeconds() + 30;
        }

        internal static void SuspendQueuedWork()
        {
            embeddingClient.CancelQueuedRequests();
            if (compactionCancellation != null) compactionCancellation.Cancel();
            RunStorage(FlushAccessMetadata);
        }

        private static void SaveRecentLocked()
        {
            Directory.CreateDirectory(StorageFolderPath);
            OrcaMemoryStore.SaveRecent(StorageRecentPath, recentExperiences);
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
