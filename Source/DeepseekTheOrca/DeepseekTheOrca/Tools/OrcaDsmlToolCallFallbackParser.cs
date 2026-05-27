using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DeepseekTheOrca
{
    internal static class OrcaDsmlToolCallFallbackParser
    {
        private const string DsmlPrefix = @"(?:[|\uFF5C]\s*){1,2}DSML\s*(?:[|\uFF5C]\s*){1,2}";
        private static readonly Regex ToolCallsPattern = new Regex(
            @"<\s*" + DsmlPrefix + @"tool_calls\s*>(?<body>.*?)</\s*" + DsmlPrefix + @"tool_calls\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex InvokePattern = new Regex(
            @"<\s*" + DsmlPrefix + @"invoke\b[^>]*\bname\s*=\s*[""'](?<name>[^""']+)[""'][^>]*>(?<body>.*?)</\s*" + DsmlPrefix + @"invoke\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex ParameterPattern = new Regex(
            @"<\s*" + DsmlPrefix + @"parameter\b[^>]*\bname\s*=\s*[""'](?<name>[^""']+)[""'][^>]*>(?<value>.*?)</\s*" + DsmlPrefix + @"parameter\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        public static List<LlmToolCall> ParseToolCalls(string content)
        {
            List<LlmToolCall> result = new List<LlmToolCall>();
            if (string.IsNullOrEmpty(content))
            {
                return result;
            }

            foreach (Match blockMatch in ToolCallsPattern.Matches(content))
            {
                string blockBody = blockMatch.Groups["body"].Value;
                foreach (Match invokeMatch in InvokePattern.Matches(blockBody))
                {
                    string name = invokeMatch.Groups["name"].Value.Trim();
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    result.Add(new LlmToolCall
                    {
                        id = "dsml_fallback_tool_call_" + (result.Count + 1),
                        name = name,
                        argumentsJson = MiniJson.Serialize(ParseArguments(invokeMatch.Groups["body"].Value))
                    });
                }
            }

            return result;
        }

        public static string StripToolCalls(string content)
        {
            return string.IsNullOrEmpty(content) ? "" : ToolCallsPattern.Replace(content, "").Trim();
        }

        private static Dictionary<string, object> ParseArguments(string invokeBody)
        {
            Dictionary<string, object> arguments = new Dictionary<string, object>();
            foreach (Match parameterMatch in ParameterPattern.Matches(invokeBody ?? ""))
            {
                string name = NormalizeArgumentName(parameterMatch.Groups["name"].Value.Trim());
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                arguments[name] = WebUtility.HtmlDecode(parameterMatch.Groups["value"].Value.Trim());
            }

            return arguments;
        }

        private static string NormalizeArgumentName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOf('_') < 0)
            {
                return name ?? "";
            }

            StringBuilder builder = new StringBuilder();
            bool upperNext = false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '_')
                {
                    upperNext = builder.Length > 0;
                    continue;
                }

                builder.Append(upperNext ? char.ToUpperInvariant(c) : c);
                upperNext = false;
            }

            return builder.ToString();
        }
    }
}
