using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;
namespace DeepseekTheOrca
{
    public sealed class OrcaChatLine
    {
        public readonly string Speaker;
        public string Text;

        public OrcaChatLine(string speaker, string text)
        {
            Speaker = speaker;
            Text = text;
        }
    }

    public sealed class OrcaChatTurnLog
    {
        public readonly int Sequence;
        public readonly string UserText;
        public string ProcessText = "";
        public string ReplyText = "";
        public string ErrorText = "";

        public OrcaChatTurnLog(int sequence, string userText)
        {
            Sequence = sequence;
            UserText = userText ?? "";
        }

        public string Label
        {
            get
            {
                string text = StripRichTextTags(UserText).Replace("\n", " ").Replace("\r", " ");
                if (text.Length > 24)
                {
                    text = text.Substring(0, 24) + "...";
                }

                return "#" + Sequence + " " + text;
            }
        }

        private static string StripRichTextTags(string text)
        {
            if (text.NullOrEmpty())
            {
                return "";
            }

            StringBuilder builder = new StringBuilder(text.Length);
            bool inTag = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '<')
                {
                    inTag = true;
                    continue;
                }

                if (ch == '>' && inTag)
                {
                    inTag = false;
                    continue;
                }

                if (!inTag)
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }
    }

    public sealed class OrcaChatReply
    {
        public string reply;
        public int moodDelta;
        public bool parsedJson;

        public string HistoryContent(string originalContent)
        {
            if (parsedJson && !OrcaVisibleReplySanitizer.ContainsControlMarkup(originalContent))
            {
                return originalContent ?? "";
            }

            Dictionary<string, object> normalized = new Dictionary<string, object>();
            normalized["reply"] = reply ?? "";
            normalized["moodDelta"] = moodDelta;
            return MiniJson.Serialize(normalized);
        }

        public static OrcaChatReply Parse(string content)
        {
            if (content.NullOrEmpty())
            {
                return new OrcaChatReply { reply = "", moodDelta = 0, parsedJson = false };
            }

            try
            {
                Dictionary<string, object> parsed = MiniJson.Deserialize(ExtractJsonObject(content)) as Dictionary<string, object>;
                if (parsed == null)
                {
                    return new OrcaChatReply { reply = content, moodDelta = 0, parsedJson = false };
                }

                string reply = GetString(parsed, "reply");
                int moodDelta = ClampMoodDelta(GetInt(parsed, "moodDelta"));
                return new OrcaChatReply
                {
                    reply = reply.NullOrEmpty() ? content : reply,
                    moodDelta = moodDelta,
                    parsedJson = true
                };
            }
            catch
            {
                return new OrcaChatReply { reply = content, moodDelta = 0, parsedJson = false };
            }
        }

        private static string ExtractJsonObject(string content)
        {
            int start = content.IndexOf('{');
            int end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return content.Substring(start, end - start + 1);
            }

            return content;
        }

        private static string GetString(Dictionary<string, object> parsed, string key)
        {
            object value;
            if (!parsed.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            return value.ToString();
        }

        private static int GetInt(Dictionary<string, object> parsed, string key)
        {
            object value;
            if (!parsed.TryGetValue(key, out value) || value == null)
            {
                return 0;
            }

            int intValue;
            if (int.TryParse(value.ToString(), out intValue))
            {
                return intValue;
            }

            float floatValue;
            if (float.TryParse(value.ToString(), out floatValue))
            {
                return Mathf.RoundToInt(floatValue);
            }

            return 0;
        }

        private static int ClampMoodDelta(int value)
        {
            if (value < -10)
            {
                return -10;
            }

            if (value > 10)
            {
                return 10;
            }

            return value;
        }
    }
}
