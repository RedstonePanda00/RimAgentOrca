using System;
using System.IO;
using System.Security;
using Verse;

namespace DeepseekTheOrca
{
    // Persistence failures stop this service, not the game update or chat workflow.
    public static partial class OrcaLongTermMemoryService
    {
        private static string storageError = "";
        private static Func<string> configFolder = () => GenFilePaths.ConfigFolderPath;
        private static string StorageFolderPath { get { return Path.Combine(configFolder(), "DeepseekTheOrca", "Memory", loadedPersonaKey); } }
        private static string StorageJournalPath { get { return Path.Combine(StorageFolderPath, "compaction_commit.json"); } }
        private static string StorageStatePath { get { return Path.Combine(StorageFolderPath, "compaction_state.json"); } }
        private static string StorageChunkPath { get { return Path.Combine(StorageFolderPath, "memory_chunks.jsonl"); } }
        private static string StorageClusterPath { get { return Path.Combine(StorageFolderPath, "memory_clusters.jsonl"); } }
        private static string StorageRecentPath { get { return Path.Combine(StorageFolderPath, "recent_experience.jsonl"); } }
        private static string StorageIndexPath { get { return Path.Combine(StorageFolderPath, "keyword_index.json"); } }

        public static bool CompactionPaused
        {
            get { RunStorage(EnsureLoaded); return storageError.Length > 0 || compactionState.Paused; }
        }

        public static string CompactionStatus
        {
            get
            {
                RunStorage(EnsureLoaded);
                if (storageError.Length > 0) return "DTO_MemoryStoragePaused".Translate(loadedPersonaKey, storageError);
                if (compactionState.Paused) return "DTO_MemoryCompactionPaused".Translate(compactionState.error);
                if (!compactionState.error.NullOrEmpty())
                    return "DTO_MemoryCompactionRetry".Translate(Math.Max(0, compactionState.retryAt - OrcaMemoryRecord.NowUnixSeconds()), compactionState.error);
                return "";
            }
        }

        public static void ResumeCompaction()
        {
            bool recoveringStorage = storageError.Length > 0;
            storageError = "";
            RunStorage(() =>
            {
                // Retry the old owner's in-memory changes before allowing persona reload.
                // Never overwrite unread files after a failed initial load.
                if (recoveringStorage && loaded)
                {
                    if (pendingCommitChunks != null) CompleteCompactionCommit();
                    SaveRecordsLocked();
                    SaveRecentLocked();
                    SaveCompactionStateLocked();
                    keywordIndex.Rebuild(records);
                    keywordIndex.Save(StorageIndexPath);
                }
                EnsureLoaded();
                if (!recoveringStorage && pendingCompaction == null)
                {
                    compactionState.Reset();
                    SaveCompactionStateLocked();
                }
            });
        }

        private static void RunStorage(Action action)
        {
            RunStorage(() => { action(); return true; }, false);
        }

        private static T RunStorage<T>(Func<T> action, T fallback)
        {
            if (storageError.Length > 0) return fallback;
            try { return action(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                storageError = ex.GetType().Name + ": " + ex.Message;
                embeddingClient.CancelQueuedRequests();
                if (compactionCancellation != null) compactionCancellation.Cancel();
                // A failed pre-dispatch state write must not leave a phantom request.
                if (pendingCompaction == null && compactionState.inFlight && pendingCommitChunks == null)
                    compactionState.CancelQueued();
                Log.Warning("[RimAgent] Memory storage paused for " + loadedPersonaKey + ": " + storageError);
                return fallback;
            }
        }
    }
}
