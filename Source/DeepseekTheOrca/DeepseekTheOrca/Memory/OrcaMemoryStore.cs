using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Verse;

namespace DeepseekTheOrca
{
    // JSONL persistence for memory records and recent experiences.
    public static class OrcaMemoryStore
    {
        public static List<OrcaRecentExperienceRecord> LoadRecent(string filePath)
        {
            List<OrcaRecentExperienceRecord> result = new List<OrcaRecentExperienceRecord>();
            if (!File.Exists(filePath))
            {
                return result;
            }

            foreach (string line in File.ReadAllLines(filePath))
            {
                OrcaRecentExperienceRecord record = RecentFromJson(line);
                if (record != null && !record.id.NullOrEmpty() && !record.text.NullOrEmpty())
                {
                    result.Add(record);
                }
            }

            return result;
        }

        public static List<OrcaMemoryRecord> LoadRecords(string filePath, string defaultKind)
        {
            List<OrcaMemoryRecord> result = new List<OrcaMemoryRecord>();
            if (!File.Exists(filePath))
            {
                return result;
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
                    result.Add(record);
                }
            }

            return result;
        }

        public static void SaveRecent(string filePath, List<OrcaRecentExperienceRecord> recentExperiences)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < recentExperiences.Count; i++)
            {
                builder.AppendLine(RecentToJson(recentExperiences[i]));
            }

            File.WriteAllText(filePath, builder.ToString());
        }

        public static void SaveRecords(string filePath, IEnumerable<OrcaMemoryRecord> source)
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
    }
}
