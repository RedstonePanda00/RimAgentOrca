using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class WebSearchTool : OrcaToolWorker
    {
        private const string TavilySearchUrl = "https://api.tavily.com/search";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);

        public string Name
        {
            get { return "web_search"; }
        }

        public string Description
        {
            get { return "Search the public web for current external information. This is not for RimWorld game-state data."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !settings.enableWebSearch)
            {
                return AiToolResult.Fail("web search is disabled in mod settings");
            }

            if (!settings.UsesLocalWebSearchTool)
            {
                return AiToolResult.Fail("local web search tool is disabled for the selected provider mode");
            }

            if (settings.tavilyApiKey.NullOrEmpty())
            {
                return AiToolResult.Fail("Tavily API key is empty");
            }

            string query = GetArg(arguments, "query");
            if (query.NullOrEmpty())
            {
                return AiToolResult.Fail("missing argument: query");
            }

            int requestedMaxResults = ParseInt(arguments, "maxResults", settings.tavilyMaxResults);
            int maxResults = Mathf.Clamp(requestedMaxResults, 1, Mathf.Clamp(settings.tavilyMaxResults, 1, 10));
            string topic = NormalizeTopic(GetArg(arguments, "topic"));
            string timeRange = NormalizeTimeRange(GetArg(arguments, "timeRange"));

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                Dictionary<string, object> payload = new Dictionary<string, object>();
                payload["query"] = query;
                payload["search_depth"] = NormalizeSearchDepth(settings.tavilySearchDepth);
                payload["topic"] = topic;
                payload["max_results"] = maxResults;
                payload["include_answer"] = "basic";
                payload["include_raw_content"] = false;
                payload["include_images"] = false;
                payload["include_image_descriptions"] = false;
                payload["include_usage"] = true;
                if (!timeRange.NullOrEmpty())
                {
                    payload["time_range"] = timeRange;
                }

                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = Timeout;
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("RimAgent/0.1");
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.tavilyApiKey);

                    string json = MiniJson.Serialize(payload);
                    using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        HttpResponseMessage response = client.PostAsync(TavilySearchUrl, content).GetAwaiter().GetResult();
                        string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        if (!response.IsSuccessStatusCode)
                        {
                            return AiToolResult.Fail("Tavily HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + ": " + Clamp(responseText, 600));
                        }

                        SearchSummary summary = ParseTavily(responseText, maxResults);
                        if (summary.IsEmpty)
                        {
                            return AiToolResult.Ok("Tavily search completed with no concise results")
                                .WithValue("provider", "tavily")
                                .WithValue("query", query)
                                .WithValue("results", "");
                        }

                        AiToolResult result = AiToolResult.Ok("Tavily search completed")
                            .WithValue("provider", "tavily")
                            .WithValue("query", query)
                            .WithValue("answer", summary.answer)
                            .WithValue("results", string.Join(" || ", summary.results.ToArray()));
                        if (!summary.followUpQuestions.NullOrEmpty())
                        {
                            result.WithValue("followUpQuestions", summary.followUpQuestions);
                        }
                        if (!summary.usage.NullOrEmpty())
                        {
                            result.WithValue("usage", summary.usage);
                        }

                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return AiToolResult.Fail(ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static SearchSummary ParseTavily(string responseText, int maxResults)
        {
            SearchSummary summary = new SearchSummary();
            Dictionary<string, object> root = MiniJson.Deserialize(responseText) as Dictionary<string, object>;
            if (root == null)
            {
                return summary;
            }

            summary.answer = Clamp(GetString(root, "answer"), 1000);
            summary.followUpQuestions = JoinStringList(root, "follow_up_questions", 500);
            summary.usage = FormatUsage(root);

            object resultsObj;
            List<object> results = root.TryGetValue("results", out resultsObj) ? resultsObj as List<object> : null;
            if (results == null)
            {
                return summary;
            }

            for (int i = 0; i < results.Count && summary.results.Count < maxResults; i++)
            {
                Dictionary<string, object> item = results[i] as Dictionary<string, object>;
                if (item == null)
                {
                    continue;
                }

                string title = Clamp(GetString(item, "title"), 140);
                string content = Clamp(GetString(item, "content"), 700);
                string url = GetString(item, "url");
                string score = FormatNumber(item, "score");

                StringBuilder builder = new StringBuilder();
                if (!title.NullOrEmpty())
                {
                    builder.Append(title);
                }
                if (!content.NullOrEmpty())
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(": ");
                    }
                    builder.Append(content);
                }
                if (!url.NullOrEmpty())
                {
                    builder.Append(" (");
                    builder.Append(url);
                    builder.Append(")");
                }
                if (!score.NullOrEmpty())
                {
                    builder.Append(" score=");
                    builder.Append(score);
                }

                if (builder.Length > 0)
                {
                    summary.results.Add(builder.ToString());
                }
            }

            return summary;
        }

        private static string FormatUsage(Dictionary<string, object> root)
        {
            object value;
            Dictionary<string, object> usage = root.TryGetValue("usage", out value) ? value as Dictionary<string, object> : null;
            if (usage == null)
            {
                return "";
            }

            string credits = FormatNumber(usage, "credits");
            return credits.NullOrEmpty() ? "" : "credits=" + credits;
        }

        private static string JoinStringList(Dictionary<string, object> source, string key, int maxChars)
        {
            object value;
            List<object> list = source.TryGetValue(key, out value) ? value as List<object> : null;
            if (list == null || list.Count == 0)
            {
                return "";
            }

            List<string> strings = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                {
                    strings.Add(list[i].ToString());
                }
            }

            return Clamp(string.Join(" | ", strings.ToArray()), maxChars);
        }

        private static string NormalizeSearchDepth(string value)
        {
            if (value == "advanced" || value == "fast" || value == "ultra-fast")
            {
                return value;
            }

            return "basic";
        }

        private static string NormalizeTopic(string value)
        {
            if (value == "news" || value == "finance")
            {
                return value;
            }

            return "general";
        }

        private static string NormalizeTimeRange(string value)
        {
            if (value == "day" || value == "week" || value == "month" || value == "year" || value == "d" || value == "w" || value == "m" || value == "y")
            {
                return value;
            }

            return "";
        }

        private static string GetString(Dictionary<string, object> source, string key)
        {
            object value;
            return source.TryGetValue(key, out value) && value != null ? value.ToString() : "";
        }

        private static string GetArg(Dictionary<string, string> arguments, string key)
        {
            string value;
            return arguments != null && arguments.TryGetValue(key, out value) ? value : "";
        }

        private static int ParseInt(Dictionary<string, string> arguments, string key, int defaultValue)
        {
            int value;
            return int.TryParse(GetArg(arguments, key), out value) ? value : defaultValue;
        }

        private static string FormatNumber(Dictionary<string, object> source, string key)
        {
            object value;
            return source.TryGetValue(key, out value) && value != null ? value.ToString() : "";
        }

        private static string Clamp(string text, int maxChars)
        {
            if (text == null)
            {
                return "";
            }

            return text.Length <= maxChars ? text : text.Substring(0, maxChars) + "...";
        }

        private sealed class SearchSummary
        {
            public string answer = "";
            public string followUpQuestions = "";
            public string usage = "";
            public readonly List<string> results = new List<string>();

            public bool IsEmpty
            {
                get { return answer.NullOrEmpty() && results.Count == 0; }
            }
        }
    }
}

