using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
namespace DeepseekTheOrca
{
    public sealed partial class LlmApiClient
    {
        private static void ParseStreamingChunk(string data, LlmStreamingChatRequest streamingRequest)
        {
            try
            {
                Dictionary<string, object> root = MiniJson.Deserialize(data) as Dictionary<string, object>;
                object choicesObj;
                List<object> choices = root != null && root.TryGetValue("choices", out choicesObj) ? choicesObj as List<object> : null;
                if (choices == null || choices.Count == 0)
                {
                    return;
                }

                Dictionary<string, object> firstChoice = choices[0] as Dictionary<string, object>;
                Dictionary<string, object> delta = firstChoice == null ? null : GetDictionary(firstChoice, "delta");
                if (delta == null)
                {
                    return;
                }

                string content = GetString(delta, "content");
                if (!string.IsNullOrEmpty(content))
                {
                    streamingRequest.AppendContent(content);
                }
            }
            catch
            {
            }
        }

        private static string BuildChatCompletionBody(string model, List<LlmChatMessage> messages, List<Dictionary<string, object>> tools, int maxTokens, float temperature, bool includeThinkingToggle, string providerId)
        {
            return BuildChatCompletionBody(model, messages, tools, maxTokens, temperature, includeThinkingToggle, providerId, stream: false);
        }

        private static string BuildChatCompletionBody(string model, List<LlmChatMessage> messages, List<Dictionary<string, object>> tools, int maxTokens, float temperature, bool includeThinkingToggle, string providerId, bool stream)
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = model;
            body["messages"] = BuildMessageArray(messages);
            if (tools != null && tools.Count > 0)
            {
                body["tools"] = tools;
                body["tool_choice"] = "auto";
            }
            if (includeThinkingToggle)
            {
                body["thinking"] = new Dictionary<string, object>
                {
                    { "type", "disabled" }
                };
            }
            if (!UsesDefaultOnlyTemperature(providerId, model))
            {
                body["temperature"] = temperature;
            }
            body[UsesOpenAiChatCompletionTokens(providerId) ? "max_completion_tokens" : "max_tokens"] = maxTokens;
            body["stream"] = stream;
            return MiniJson.Serialize(body);
        }

        private static List<Dictionary<string, object>> BuildMessageArray(List<LlmChatMessage> messages)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            for (int i = 0; i < messages.Count; i++)
            {
                LlmChatMessage message = messages[i];
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["role"] = message.role;
                if (message.content != null)
                {
                    item["content"] = message.content;
                }
                else if (message.role != "assistant")
                {
                    item["content"] = "";
                }

                if (!string.IsNullOrEmpty(message.toolCallId))
                {
                    item["tool_call_id"] = message.toolCallId;
                }

                if (message.toolCalls != null && message.toolCalls.Count > 0)
                {
                    List<Dictionary<string, object>> toolCalls = new List<Dictionary<string, object>>();
                    foreach (LlmToolCall toolCall in message.toolCalls)
                    {
                        toolCalls.Add(new Dictionary<string, object>
                        {
                            { "id", toolCall.id },
                            { "type", "function" },
                            { "function", new Dictionary<string, object>
                                {
                                    { "name", toolCall.name },
                                    { "arguments", toolCall.argumentsJson ?? "{}" }
                                }
                            }
                        });
                    }
                    item["tool_calls"] = toolCalls;
                }

                result.Add(item);
            }

            return result;
        }

        private static bool AllowDsmlToolCallFallback(OrcaLlmModelRole role)
        {
            return role == OrcaLlmModelRole.Tool
                || role == OrcaLlmModelRole.WebSearch
                || role == OrcaLlmModelRole.Decision;
        }

        private static LlmChatResponse ParseChatResponse(string responseText, OrcaLlmModelRole role)
        {
            try
            {
                Dictionary<string, object> root = MiniJson.Deserialize(responseText) as Dictionary<string, object>;
                object choicesObj;
                if (root == null || !root.TryGetValue("choices", out choicesObj))
                {
                    return LlmChatResponse.Failure("Response did not contain choices.");
                }

                List<object> choices = choicesObj as List<object>;
                if (choices == null || choices.Count == 0)
                {
                    return LlmChatResponse.Failure("Response choices were empty.");
                }

                Dictionary<string, object> firstChoice = choices[0] as Dictionary<string, object>;
                object messageObj;
                if (firstChoice == null || !firstChoice.TryGetValue("message", out messageObj))
                {
                    return LlmChatResponse.Failure("Response did not contain a message.");
                }

                Dictionary<string, object> message = messageObj as Dictionary<string, object>;
                if (message == null)
                {
                    return LlmChatResponse.Failure("Response message was malformed.");
                }

                LlmChatResponse parsed = LlmChatResponse.Success();
                Dictionary<string, object> usage = GetDictionary(root, "usage");
                if (usage != null)
                {
                    parsed.promptTokens = GetInt(usage, "prompt_tokens");
                    parsed.completionTokens = GetInt(usage, "completion_tokens");
                    parsed.totalTokens = GetInt(usage, "total_tokens");
                    parsed.cacheHitTokens = GetInt(usage, "prompt_cache_hit_tokens");
                    parsed.cacheMissTokens = GetInt(usage, "prompt_cache_miss_tokens");
                    Dictionary<string, object> promptTokenDetails = GetDictionary(usage, "prompt_tokens_details");
                    if (promptTokenDetails != null)
                    {
                        parsed.cacheHitTokens = GetInt(promptTokenDetails, "cached_tokens");
                        if (parsed.promptTokens > 0)
                        {
                            parsed.cacheMissTokens = Math.Max(0, parsed.promptTokens - parsed.cacheHitTokens);
                        }
                    }

                    if (parsed.totalTokens == 0)
                    {
                        parsed.totalTokens = parsed.promptTokens + parsed.completionTokens;
                    }
                }

                object contentObj;
                if (message.TryGetValue("content", out contentObj) && contentObj != null)
                {
                    parsed.content = contentObj.ToString();
                }

                object toolCallsObj;
                if (message.TryGetValue("tool_calls", out toolCallsObj))
                {
                    List<object> toolCalls = toolCallsObj as List<object>;
                    if (toolCalls != null)
                    {
                        foreach (object toolCallObj in toolCalls)
                        {
                            Dictionary<string, object> toolCall = toolCallObj as Dictionary<string, object>;
                            if (toolCall == null)
                            {
                                continue;
                            }

                            Dictionary<string, object> function = GetDictionary(toolCall, "function");
                            if (function == null)
                            {
                                continue;
                            }

                            LlmToolCall parsedToolCall = new LlmToolCall();
                            parsedToolCall.id = GetString(toolCall, "id");
                            parsedToolCall.name = GetString(function, "name");
                            parsedToolCall.argumentsJson = GetString(function, "arguments") ?? "{}";
                            parsed.toolCalls.Add(parsedToolCall);
                        }
                    }
                }

                if (AllowDsmlToolCallFallback(role) && parsed.toolCalls.Count == 0 && !string.IsNullOrEmpty(parsed.content))
                {
                    List<LlmToolCall> dsmlToolCalls = OrcaDsmlToolCallFallbackParser.ParseToolCalls(parsed.content);
                    if (dsmlToolCalls.Count > 0)
                    {
                        parsed.toolCalls.AddRange(dsmlToolCalls);
                        parsed.content = OrcaDsmlToolCallFallbackParser.StripToolCalls(parsed.content);
                    }
                }

                return parsed;
            }
            catch (Exception ex)
            {
                return LlmChatResponse.Failure("Failed to parse chat response: " + ex.Message);
            }
        }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> source, string key)
        {
            object value;
            if (!source.TryGetValue(key, out value))
            {
                return null;
            }

            return value as Dictionary<string, object>;
        }

        private static string GetString(Dictionary<string, object> source, string key)
        {
            object value;
            if (!source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            return value.ToString();
        }

        private static int GetInt(Dictionary<string, object> source, string key)
        {
            object value;
            if (!source.TryGetValue(key, out value) || value == null)
            {
                return 0;
            }

            if (value is int)
            {
                return (int)value;
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

        private static List<string> ParseModelIds(string responseText)
        {
            List<string> models = new List<string>();
            try
            {
                Dictionary<string, object> root = MiniJson.Deserialize(responseText) as Dictionary<string, object>;
                if (root == null)
                {
                    return models;
                }

                object dataObj;
                List<object> data = root.TryGetValue("data", out dataObj) ? dataObj as List<object> : null;
                if (data == null)
                {
                    return models;
                }

                foreach (object itemObj in data)
                {
                    Dictionary<string, object> item = itemObj as Dictionary<string, object>;
                    if (item == null)
                    {
                        continue;
                    }

                    string id = GetString(item, "id");
                    if (!string.IsNullOrEmpty(id) && !models.Contains(id))
                    {
                        models.Add(id);
                    }
                }
            }
            catch
            {
            }

            models.Sort();
            return models;
        }

        private static OrcaEmbeddingResult ParseEmbeddingResponse(string responseText)
        {
            try
            {
                Dictionary<string, object> root = MiniJson.Deserialize(responseText) as Dictionary<string, object>;
                object dataObj;
                List<object> data = root != null && root.TryGetValue("data", out dataObj) ? dataObj as List<object> : null;
                if (data == null || data.Count == 0)
                {
                    return OrcaEmbeddingResult.Failure("Embedding response did not contain data.");
                }

                Dictionary<string, object> first = data[0] as Dictionary<string, object>;
                object embeddingObj;
                List<object> raw = first != null && first.TryGetValue("embedding", out embeddingObj) ? embeddingObj as List<object> : null;
                if (raw == null || raw.Count == 0)
                {
                    return OrcaEmbeddingResult.Failure("Embedding response did not contain an embedding vector.");
                }

                OrcaEmbeddingResult result = new OrcaEmbeddingResult { success = true };
                for (int i = 0; i < raw.Count; i++)
                {
                    object value = raw[i];
                    if (value is double)
                    {
                        result.embedding.Add((float)(double)value);
                    }
                    else if (value is long)
                    {
                        result.embedding.Add((float)(long)value);
                    }
                    else
                    {
                        float parsed;
                        if (value != null && float.TryParse(value.ToString(), out parsed))
                        {
                            result.embedding.Add(parsed);
                        }
                    }
                }

                return result.embedding.Count == 0 ? OrcaEmbeddingResult.Failure("Embedding vector was empty.") : result;
            }
            catch (Exception ex)
            {
                return OrcaEmbeddingResult.Failure("Failed to parse embedding response: " + ex.Message);
            }
        }

        private static bool UsesOpenAiChatCompletionTokens(string providerId)
        {
            return LlmProviderConfig.NormalizeProvider(providerId) == LlmProviderConfig.OpenAI;
        }

        private static bool UsesDefaultOnlyTemperature(string providerId, string model)
        {
            if (LlmProviderConfig.NormalizeProvider(providerId) != LlmProviderConfig.OpenAI || string.IsNullOrEmpty(model))
            {
                return false;
            }

            string lower = model.ToLowerInvariant();
            return lower.StartsWith("o1")
                || lower.StartsWith("o3")
                || lower.StartsWith("o4")
                || lower.StartsWith("gpt-5");
        }

        private static void ApplyAuthorizationHeaders(HttpClient client, string apiKey, string openAiOrganization, string openAiProject)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            if (!string.IsNullOrWhiteSpace(openAiOrganization))
            {
                client.DefaultRequestHeaders.Add("OpenAI-Organization", openAiOrganization.Trim());
            }

            if (!string.IsNullOrWhiteSpace(openAiProject))
            {
                client.DefaultRequestHeaders.Add("OpenAI-Project", openAiProject.Trim());
            }
        }

        private static void ApplyTransportHeaders(HttpClient client)
        {
            if (client == null)
            {
                return;
            }

            client.DefaultRequestHeaders.ConnectionClose = true;
        }

        private static bool IsTransientTransportException(Exception ex)
        {
            return ex is HttpRequestException
                || ex is ObjectDisposedException
                || ex is IOException;
        }

        private static string TransportFailureMessage(Exception ex)
        {
            return ex == null ? "Transport error." : ex.GetType().Name + ": " + ex.Message;
        }

        private static string TransientRetryMessage(Exception ex, int attempt)
        {
            return "Transient transport error on attempt " + attempt + "; retrying once: " + TransportFailureMessage(ex);
        }

        private static HttpClient CreateHttpClient(string proxyUrl)
        {
            if (string.IsNullOrWhiteSpace(proxyUrl))
            {
                return new HttpClient();
            }

            HttpClientHandler handler = new HttpClientHandler();
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(proxyUrl.Trim());
            return new HttpClient(handler);
        }

        private static string ExtractErrorMessage(string responseText)
        {
            if (string.IsNullOrEmpty(responseText))
            {
                return "";
            }

            Match match = Regex.Match(responseText, "\"message\"\\s*:\\s*\"(?<message>(?:\\\\.|[^\"])*)\"");
            if (!match.Success)
            {
                return responseText.Length > 300 ? responseText.Substring(0, 300) : responseText;
            }

            return Regex.Unescape(match.Groups["message"].Value);
        }

        private static string JsonEscape(string value)
        {
            if (value == null)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
