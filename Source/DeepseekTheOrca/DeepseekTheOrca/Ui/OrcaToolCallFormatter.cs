using System;
using System.Collections.Generic;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaToolCallFormatter
    {
        public static Dictionary<string, string> ParseArguments(string argumentsJson)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (argumentsJson.NullOrEmpty())
            {
                return result;
            }

            result["__rawJson"] = argumentsJson;
            try
            {
                Dictionary<string, object> parsed = MiniJson.Deserialize(argumentsJson) as Dictionary<string, object>;
                if (parsed == null)
                {
                    return result;
                }

                foreach (KeyValuePair<string, object> pair in parsed)
                {
                    result[pair.Key] = pair.Value == null ? "" : pair.Value.ToString();
                }
            }
            catch (Exception ex)
            {
                result["parseError"] = ex.Message;
            }

            return result;
        }

        public static string FormatArguments(Dictionary<string, string> arguments)
        {
            if (arguments == null || arguments.Count == 0)
            {
                return "{}";
            }

            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string> pair in arguments)
            {
                if (pair.Key == "__rawJson")
                {
                    continue;
                }

                parts.Add(pair.Key + "=" + pair.Value);
            }

            return "{" + string.Join(", ", parts.ToArray()) + "}";
        }

        public static string ToolCallHint(LlmChatResponse response)
        {
            if (response == null || response.toolCalls == null || response.toolCalls.Count == 0)
            {
                return "none";
            }

            List<string> parts = new List<string>();
            for (int i = 0; i < response.toolCalls.Count; i++)
            {
                LlmToolCall toolCall = response.toolCalls[i];
                if (toolCall == null)
                {
                    continue;
                }

                parts.Add((toolCall.name ?? "") + " " + (toolCall.argumentsJson ?? "{}"));
            }

            return parts.Count == 0 ? "none" : string.Join(" | ", parts.ToArray());
        }
    }
}
