using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaMemoryKeywordIndex
    {
        private const int Version = 1;
        private readonly Dictionary<string, List<string>> keywordToIds = new Dictionary<string, List<string>>();
        private string memoryHash = "";

        public List<string> FindIds(List<string> keywords)
        {
            List<string> result = new List<string>();
            if (keywords == null)
            {
                return result;
            }

            for (int i = 0; i < keywords.Count; i++)
            {
                string key = Normalize(keywords[i]);
                List<string> ids;
                if (key.NullOrEmpty() || !keywordToIds.TryGetValue(key, out ids))
                {
                    continue;
                }

                result.AddRange(ids);
            }

            return result.Distinct().ToList();
        }

        public void Rebuild(List<OrcaMemoryRecord> records)
        {
            keywordToIds.Clear();
            memoryHash = ComputeMemoryHash(records);
            if (records == null)
            {
                return;
            }

            for (int i = 0; i < records.Count; i++)
            {
                OrcaMemoryRecord record = records[i];
                if (!ShouldIndex(record))
                {
                    continue;
                }

                List<string> keywords = record.keywords ?? new List<string>();
                for (int j = 0; j < keywords.Count; j++)
                {
                    string key = Normalize(keywords[j]);
                    if (key.NullOrEmpty())
                    {
                        continue;
                    }

                    List<string> ids;
                    if (!keywordToIds.TryGetValue(key, out ids))
                    {
                        ids = new List<string>();
                        keywordToIds[key] = ids;
                    }
                    if (!ids.Contains(record.id))
                    {
                        ids.Add(record.id);
                    }
                }
            }
        }

        public bool TryLoad(string filePath, List<OrcaMemoryRecord> records)
        {
            try
            {
                if (filePath.NullOrEmpty() || !File.Exists(filePath))
                {
                    return false;
                }

                Dictionary<string, object> root = MiniJson.Deserialize(File.ReadAllText(filePath)) as Dictionary<string, object>;
                if (root == null || GetInt(root, "version") != Version)
                {
                    return false;
                }

                string expectedHash = ComputeMemoryHash(records);
                if (GetString(root, "memoryHash") != expectedHash)
                {
                    return false;
                }

                Dictionary<string, object> rawIndex = GetDictionary(root, "index");
                if (rawIndex == null)
                {
                    return false;
                }

                keywordToIds.Clear();
                foreach (KeyValuePair<string, object> pair in rawIndex)
                {
                    List<object> rawIds = pair.Value as List<object>;
                    if (rawIds == null)
                    {
                        continue;
                    }

                    keywordToIds[Normalize(pair.Key)] = rawIds.Where(item => item != null).Select(item => item.ToString()).Where(item => !item.NullOrEmpty()).Distinct().ToList();
                }

                memoryHash = expectedHash;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Save(string filePath)
        {
            if (filePath.NullOrEmpty())
            {
                return;
            }

            Dictionary<string, object> root = new Dictionary<string, object>();
            root["version"] = Version;
            root["memoryHash"] = memoryHash;
            Dictionary<string, object> index = new Dictionary<string, object>();
            foreach (KeyValuePair<string, List<string>> pair in keywordToIds)
            {
                index[pair.Key] = pair.Value ?? new List<string>();
            }
            root["index"] = index;

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, MiniJson.Serialize(root));
        }

        public static string ComputeMemoryHash(List<OrcaMemoryRecord> records)
        {
            StringBuilder builder = new StringBuilder();
            if (records != null)
            {
                foreach (OrcaMemoryRecord record in records.OrderBy(record => record == null ? "" : record.id))
                {
                    if (record == null)
                    {
                        continue;
                    }

                    builder.Append(record.id).Append('|')
                        .Append(record.memoryKind).Append('|')
                        .Append(record.clusterId).Append('|')
                        .Append(record.embeddingState).Append('|')
                        .Append(record.consolidationState).Append('|')
                        .Append(record.occurrenceCount).Append('|')
                        .Append(record.importance.ToString("0.000")).Append('|')
                        .Append(string.Join(",", (record.keywords ?? new List<string>()).ToArray())).Append(';');
                }
            }

            int hash = builder.ToString().GetHashCode();
            return hash.ToString("x8");
        }

        private static bool ShouldIndex(OrcaMemoryRecord record)
        {
            if (record == null || record.id.NullOrEmpty() || record.consolidationState == "compressed" || record.consolidationState == "pruned")
            {
                return false;
            }

            if ((record.memoryKind == "atomic" || record.memoryKind == "chunk") && record.embeddingState != "ready")
            {
                return false;
            }

            return true;
        }

        private static string Normalize(string value)
        {
            return (value ?? "").Trim().ToLowerInvariant();
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

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> root, string key)
        {
            object value;
            return root.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }
    }
}
