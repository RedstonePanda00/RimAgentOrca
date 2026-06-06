using System;
using System.Collections.Generic;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaMemoryRecord
    {
        public string id = "";
        public string personaId = "";
        public List<string> saveIds = new List<string>();
        public int tickFirst;
        public int tickLast;
        public List<string> sourceKinds = new List<string>();
        public string fuzzySummary = "";
        public string exemplarText = "";
        public List<string> tags = new List<string>();
        public List<string> keywords = new List<string>();
        public float importance;
        public int occurrenceCount = 1;
        public List<float> centroidEmbedding = new List<float>();
        public long createdAt;
        public long lastAccessed;
        public string embeddingState = "pending";
        public string memoryKind = "atomic";
        public string clusterId = "";
        public string sourceRange = "";
        public float strength = 1f;
        public string consolidationState = "active";
        public long lastConsolidated;
        public List<string> representativeMemoryIds = new List<string>();
        public int embeddingRetryCount;
        public long nextEmbeddingRetryAt;

        public string DisplayText
        {
            get { return memoryKind == "cluster" && !fuzzySummary.NullOrEmpty() ? fuzzySummary : exemplarText; }
        }

        public static long NowUnixSeconds()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        }
    }
}
