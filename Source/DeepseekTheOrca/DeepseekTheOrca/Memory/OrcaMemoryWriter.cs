using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaMemoryWriter
    {
        private const float WriteThreshold = 0.58f;

        public static bool TryBuildRecord(string source, string text, out OrcaMemoryRecord record)
        {
            record = null;
            text = (text ?? "").Trim();
            if (text.NullOrEmpty())
            {
                return false;
            }

            List<string> tags = TagsFor(source, text);
            List<string> keywords = KeywordsFor(source, text, tags);
            float importance = ImportanceFor(source, text, tags);
            if (importance < WriteThreshold)
            {
                return false;
            }

            long now = OrcaMemoryRecord.NowUnixSeconds();
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            string saveId = OrcaLongTermMemoryService.CurrentSaveId();
            record = new OrcaMemoryRecord
            {
                id = Guid.NewGuid().ToString("N"),
                personaId = OrcaLongTermMemoryService.CurrentPersonaId(),
                saveIds = saveId.NullOrEmpty() ? new List<string>() : new List<string> { saveId },
                tickFirst = tick,
                tickLast = tick,
                sourceKinds = CleanList(new List<string> { NormalizeSource(source) }),
                fuzzySummary = "",
                exemplarText = Clamp(AbstractText(text), 520),
                tags = tags,
                keywords = keywords,
                importance = importance,
                occurrenceCount = 1,
                createdAt = now,
                lastAccessed = now,
                embeddingState = "pending",
                memoryKind = "atomic",
                strength = importance,
                consolidationState = "active"
            };
            return true;
        }

        public static OrcaMemoryRecord BuildChunkRecord(string source, string text, string sourceRange)
        {
            text = (text ?? "").Trim();
            List<string> tags = TagsFor(source, text);
            List<string> keywords = KeywordsFor(source, text, tags);
            float importance = Math.Max(0.25f, ImportanceFor(source, text, tags));
            long now = OrcaMemoryRecord.NowUnixSeconds();
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            string saveId = OrcaLongTermMemoryService.CurrentSaveId();
            return new OrcaMemoryRecord
            {
                id = Guid.NewGuid().ToString("N"),
                personaId = OrcaLongTermMemoryService.CurrentPersonaId(),
                saveIds = saveId.NullOrEmpty() ? new List<string>() : new List<string> { saveId },
                tickFirst = tick,
                tickLast = tick,
                sourceKinds = CleanList(new List<string> { NormalizeSource(source) }),
                fuzzySummary = "",
                exemplarText = Clamp(AbstractText(text), 900),
                tags = tags,
                keywords = keywords,
                importance = importance,
                occurrenceCount = 1,
                createdAt = now,
                lastAccessed = now,
                embeddingState = "pending",
                memoryKind = "chunk",
                sourceRange = sourceRange ?? "",
                strength = importance,
                consolidationState = "active"
            };
        }

        public static List<string> QueryKeywords(string text)
        {
            return KeywordsFor("query", text ?? "", TagsFor("query", text ?? ""));
        }

        public static string BuildFuzzySummary(List<string> tags, int occurrenceCount)
        {
            string theme = ThemeFor(tags);
            if (occurrenceCount <= 1)
            {
                return "This persona has a fuzzy impression of " + theme + " becoming part of the colony's story.";
            }

            return "This persona has a recurring impression that " + theme + " has happened more than once in the colony's story.";
        }

        public static float ImportanceFor(string source, string text, List<string> tags)
        {
            float score = 0f;
            string lower = text.ToLowerInvariant();
            source = (source ?? "").ToLowerInvariant();

            if (source.Contains("proactive") || source.Contains("tool_schedule") || source.Contains("tool_trigger") || source.Contains("incident"))
            {
                score += 0.25f;
            }
            if (source.Contains("agent_reply"))
            {
                score += 0.1f;
            }
            if (ContainsAny(lower, "remember", "don't forget", "preference", "i prefer", "记住", "别忘", "我喜欢", "偏好"))
            {
                score += 0.55f;
            }
            if (tags.Contains("death") || tags.Contains("funeral") || tags.Contains("raid") || tags.Contains("betrayal") || tags.Contains("promise") || tags.Contains("mental_break") || tags.Contains("medical_crisis") || tags.Contains("shortage"))
            {
                score += 0.35f;
            }
            if (tags.Contains("colony_loss") || tags.Contains("relationship") || tags.Contains("recovery") || tags.Contains("victory"))
            {
                score += 0.22f;
            }
            if (tags.Count >= 2)
            {
                score += 0.1f;
            }
            if (text.Length > 240)
            {
                score += 0.08f;
            }

            return Math.Min(1f, score);
        }

        public static List<string> TagsFor(string source, string text)
        {
            List<string> tags = new List<string>();
            string lower = (source + " " + text).ToLowerInvariant();
            AddIf(tags, lower, "preference", "prefer", "preference", "喜欢", "偏好");
            AddIf(tags, lower, "death", "death", "died", "dead", "死亡", "死了");
            AddIf(tags, lower, "funeral", "funeral", "burial", "葬礼", "安葬");
            AddIf(tags, lower, "raid", "raid", "raider", "袭击", "突袭");
            AddIf(tags, lower, "betrayal", "betray", "betrayal", "背叛");
            AddIf(tags, lower, "promise", "promise", "remember", "承诺", "记住");
            AddIf(tags, lower, "relationship", "relationship", "friend", "enemy", "lover", "关系", "朋友", "敌人");
            AddIf(tags, lower, "shortage", "shortage", "hunger", "food", "medicine", "短缺", "饥饿", "食物", "药");
            AddIf(tags, lower, "medical_crisis", "injury", "pain", "bleed", "disease", "plague", "受伤", "疼痛", "流血", "疾病");
            AddIf(tags, lower, "mental_break", "mental", "break", "breakdown", "崩溃", "精神");
            AddIf(tags, lower, "recovery", "recover", "survive", "healed", "恢复", "幸存");
            AddIf(tags, lower, "victory", "victory", "won", "defeated", "胜利", "打败");
            AddIf(tags, lower, "colony_loss", "loss", "lost", "ruin", "失败", "失去", "损失");
            AddIf(tags, lower, "colony_pressure", "colony", "colonist", "pawn", "殖民地", "殖民者");
            AddIf(tags, lower, "story_event", "story", "incident", "event", "故事", "事件");

            if (tags.Count == 0 && text.Length > 240)
            {
                tags.Add("story_event");
            }

            return tags.Take(10).ToList();
        }

        public static List<string> KeywordsFor(string source, string text, List<string> tags)
        {
            List<string> keywords = new List<string>();
            keywords.AddRange(tags ?? new List<string>());
            string normalizedSource = NormalizeSource(source);
            if (!normalizedSource.NullOrEmpty())
            {
                keywords.Add(normalizedSource);
            }

            if (tags != null && (tags.Contains("death") || tags.Contains("funeral")))
            {
                keywords.Add("loss");
                keywords.Add("grief");
            }
            if (tags != null && (tags.Contains("shortage") || tags.Contains("medical_crisis")))
            {
                keywords.Add("pressure");
            }
            if (tags != null && tags.Contains("raid"))
            {
                keywords.Add("threat");
                keywords.Add("violence");
            }

            return CleanList(keywords).Take(16).ToList();
        }

        private static string ThemeFor(List<string> tags)
        {
            if (tags == null || tags.Count == 0)
            {
                return "a meaningful colony event";
            }
            if (tags.Contains("death") || tags.Contains("funeral"))
            {
                return "death, funerals, and grief";
            }
            if (tags.Contains("raid"))
            {
                return "raids and outside violence";
            }
            if (tags.Contains("shortage"))
            {
                return "shortage and survival pressure";
            }
            if (tags.Contains("medical_crisis"))
            {
                return "injury, illness, and medical pressure";
            }
            if (tags.Contains("mental_break"))
            {
                return "mental strain and emotional collapse";
            }
            if (tags.Contains("relationship"))
            {
                return "relationships shaping the colony's story";
            }
            if (tags.Contains("promise") || tags.Contains("preference"))
            {
                return "a lasting player preference or promise";
            }
            if (tags.Contains("recovery"))
            {
                return "recovery after pressure";
            }
            return tags[0].Replace('_', ' ');
        }

        private static string AbstractText(string text)
        {
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            text = Regex.Replace(text, @"\b\d+(\.\d+)?%?\b", "some");
            text = Regex.Replace(text, @"\(\s*-?\d+\s*,\s*-?\d+\s*,\s*-?\d+\s*\)", "(somewhere)");
            text = Regex.Replace(text, @"\b[A-Z][A-Za-z0-9_\-]{2,}\b", "someone");
            return text;
        }

        public static string NormalizeSource(string source)
        {
            source = (source ?? "").Trim().ToLowerInvariant();
            if (source.NullOrEmpty())
            {
                return "";
            }

            source = Regex.Replace(source, "[^a-z0-9_:-]+", "_").Trim('_');
            return source.NullOrEmpty() ? "" : source;
        }

        private static void AddIf(List<string> tags, string lower, string tag, params string[] needles)
        {
            if (needles.Any(needle => lower.Contains(needle)) && !tags.Contains(tag))
            {
                tags.Add(tag);
            }
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            return needles.Any(needle => text.Contains(needle));
        }

        public static List<string> CleanList(List<string> values)
        {
            return values == null
                ? new List<string>()
                : values.Select(value => value == null ? "" : value.Trim().ToLowerInvariant())
                    .Where(value => !value.NullOrEmpty())
                    .Distinct()
                    .ToList();
        }

        private static string Clamp(string text, int maxChars)
        {
            if (text == null)
            {
                return "";
            }

            return text.Length <= maxChars ? text : text.Substring(0, maxChars) + "...";
        }
    }
}
