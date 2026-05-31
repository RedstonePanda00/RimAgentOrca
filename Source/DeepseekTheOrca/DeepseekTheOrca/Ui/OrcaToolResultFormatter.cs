using System.Collections.Generic;

namespace DeepseekTheOrca
{
    public static class OrcaToolResultFormatter
    {
        private const int MaxToolResultValueChars = 700;
        private const int MaxToolResultRaceDescriptionChars = 220;

        public static string FormatValues(AiToolResult result)
        {
            Dictionary<string, string> values = CompactValues(result);
            if (values.Count == 0)
            {
                return "";
            }

            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string> pair in values)
            {
                parts.Add(pair.Key + "=" + pair.Value);
            }

            return " [" + string.Join(", ", parts.ToArray()) + "]";
        }

        public static string Serialize(AiToolResult result)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["success"] = result.success;
            payload["message"] = result.message ?? "";
            payload["values"] = CompactValues(result);
            return MiniJson.Serialize(payload);
        }

        public static string MemoryText(AiToolResult result)
        {
            if (result == null)
            {
                return "";
            }

            return (result.success ? "ok" : "failed") + " - " + result.message + FormatValues(result);
        }

        private static Dictionary<string, string> CompactValues(AiToolResult result)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            if (result == null || result.values == null || result.values.Count == 0)
            {
                return values;
            }

            foreach (KeyValuePair<string, string> pair in result.values)
            {
                int limit = pair.Key == "raceDescription" ? MaxToolResultRaceDescriptionChars : MaxToolResultValueChars;
                values[pair.Key] = TruncateValue(pair.Value, limit);
            }

            return values;
        }

        private static string TruncateValue(string value, int maxChars)
        {
            if (value == null)
            {
                return "";
            }

            if (value.Length <= maxChars)
            {
                return value;
            }

            return value.Substring(0, maxChars) + "... [truncated, " + value.Length + " chars]";
        }
    }
}
