using System.Text;

namespace DeepseekTheOrca
{
    internal sealed class JsonReplyStreamExtractor
    {
        public static string SanitizeVisibleText(string text)
        {
            return OrcaVisibleReplySanitizer.Sanitize(text, trim: false);
        }

        public string Extract(string jsonPrefix)
        {
            if (string.IsNullOrEmpty(jsonPrefix))
            {
                return "";
            }

            int replyKey = FindReplyKey(jsonPrefix);
            if (replyKey < 0)
            {
                return ShouldTreatAsPlainText(jsonPrefix) ? jsonPrefix : "";
            }

            int colon = jsonPrefix.IndexOf(':', replyKey);
            if (colon < 0)
            {
                return "";
            }

            int quote = NextNonWhitespace(jsonPrefix, colon + 1);
            if (quote < 0 || quote >= jsonPrefix.Length || jsonPrefix[quote] != '"')
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            bool escaping = false;
            for (int i = quote + 1; i < jsonPrefix.Length; i++)
            {
                char c = jsonPrefix[i];
                if (escaping)
                {
                    AppendEscaped(builder, c, jsonPrefix, ref i);
                    escaping = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (c == '"')
                {
                    break;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        private static bool ShouldTreatAsPlainText(string text)
        {
            int start = NextNonWhitespace(text, 0);
            if (start < 0)
            {
                return false;
            }

            char c = text[start];
            return c != '{' && c != '[';
        }

        private static int FindReplyKey(string text)
        {
            bool inString = false;
            bool escaping = false;
            for (int i = 0; i <= text.Length - 7; i++)
            {
                char c = text[i];
                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaping = true;
                    continue;
                }

                if (c == '"')
                {
                    if (!inString && MatchesAt(text, i, "\"reply\""))
                    {
                        return i;
                    }

                    inString = !inString;
                }
            }

            return -1;
        }

        private static bool MatchesAt(string text, int index, string value)
        {
            if (index < 0 || index + value.Length > text.Length)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (text[index + i] != value[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static int NextNonWhitespace(string text, int start)
        {
            for (int i = start; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void AppendEscaped(StringBuilder builder, char c, string text, ref int index)
        {
            switch (c)
            {
                case '"':
                case '\\':
                case '/':
                    builder.Append(c);
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'u':
                    if (index + 4 < text.Length)
                    {
                        string hex = text.Substring(index + 1, 4);
                        int code;
                        if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out code))
                        {
                            builder.Append((char)code);
                            index += 4;
                        }
                    }
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }
    }
}
