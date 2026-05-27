using System.Text.RegularExpressions;

namespace DeepseekTheOrca
{
    internal static class OrcaVisibleReplySanitizer
    {
        private static readonly Regex DsmlToolCallsPattern = new Regex(
            @"<\s*(?:[|｜]\s*){1,2}DSML\s*(?:[|｜]\s*){1,2}tool_calls\s*>.*?</\s*(?:[|｜]\s*){1,2}DSML\s*(?:[|｜]\s*){1,2}tool_calls\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex DsmlTagPattern = new Regex(
            @"</?\s*(?:[|｜]\s*){1,2}DSML\s*(?:[|｜]\s*){1,2}[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex SimpleControlElementPattern = new Regex(
            @"<([A-Za-z][A-Za-z0-9_-]*)(?:\s+[^<>]*)?>[^<]*(?:</\1>)?",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex ControlTagPattern = new Regex(
            @"</?[A-Za-z][A-Za-z0-9_-]*(?:\s+[^<>]*)?/?>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex TrailingPartialControlTagPattern = new Regex(
            @"</?[A-Za-z][A-Za-z0-9_-]*(?:\s+[^<>]*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        public static string Sanitize(string text, bool trim)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            string cleaned = text;
            cleaned = DsmlToolCallsPattern.Replace(cleaned, "");
            cleaned = DsmlTagPattern.Replace(cleaned, "");
            for (int i = 0; i < 4; i++)
            {
                string next = SimpleControlElementPattern.Replace(cleaned, "");
                if (next == cleaned)
                {
                    break;
                }

                cleaned = next;
            }

            cleaned = ControlTagPattern.Replace(cleaned, "");
            cleaned = TrailingPartialControlTagPattern.Replace(cleaned, "");
            return trim ? cleaned.Trim() : cleaned;
        }

        public static bool ContainsControlMarkup(string text)
        {
            return !string.IsNullOrEmpty(text) && ControlTagPattern.IsMatch(text);
        }
    }
}
