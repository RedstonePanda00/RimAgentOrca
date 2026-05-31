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
        public bool parsedJson;
        public Dictionary<string, object> fields = new Dictionary<string, object>();

        public string HistoryContent()
        {
            Dictionary<string, object> normalized = new Dictionary<string, object>();
            normalized["reply"] = reply ?? "";
            foreach (KeyValuePair<string, object> pair in fields)
            {
                if (pair.Key.NullOrEmpty() || pair.Key == "reply")
                {
                    continue;
                }

                normalized[pair.Key] = pair.Value;
            }
            return MiniJson.Serialize(normalized);
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            object value;
            if (fields == null || !fields.TryGetValue(key, out value) || value == null)
            {
                return defaultValue;
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

            return defaultValue;
        }

        public void SetField(string key, object value)
        {
            if (key.NullOrEmpty())
            {
                return;
            }

            if (fields == null)
            {
                fields = new Dictionary<string, object>();
            }

            fields[key] = value;
        }

        public static OrcaChatReply Parse(string content)
        {
            if (content.NullOrEmpty())
            {
                return new OrcaChatReply { reply = "", parsedJson = false };
            }

            try
            {
                Dictionary<string, object> parsed = MiniJson.Deserialize(ExtractJsonObject(content)) as Dictionary<string, object>;
                if (parsed == null)
                {
                    return new OrcaChatReply { reply = content, parsedJson = false };
                }

                string reply = GetString(parsed, "reply");
                return new OrcaChatReply
                {
                    reply = reply.NullOrEmpty() ? content : reply,
                    fields = new Dictionary<string, object>(parsed),
                    parsedJson = true
                };
            }
            catch
            {
                return new OrcaChatReply { reply = content, parsedJson = false };
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

    }
}
