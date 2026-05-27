using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DeepseekTheOrca
{
    public sealed class LlmConnectionTestResult
    {
        public bool success;
        public string message;

        public static LlmConnectionTestResult Success(string message)
        {
            return new LlmConnectionTestResult { success = true, message = message };
        }

        public static LlmConnectionTestResult Failure(string message)
        {
            return new LlmConnectionTestResult { success = false, message = message };
        }
    }

    public sealed class OrcaModelDiscoveryResult
    {
        public bool success;
        public string message;
        public List<string> models = new List<string>();

        public static OrcaModelDiscoveryResult Failure(string message)
        {
            return new OrcaModelDiscoveryResult { success = false, message = message };
        }
    }

    public sealed class LlmApiClient
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan StreamingTimeout = TimeSpan.FromSeconds(120);
        private const int MaxTransportAttempts = 2;

        public async Task<LlmConnectionTestResult> TestConnectionAsync(string apiKey, string model)
        {
            return await TestConnectionAsync(apiKey, model, LlmProviderConfig.Profile(LlmProviderConfig.DeepSeek).defaultBaseUrl, LlmProviderConfig.DeepSeek).ConfigureAwait(false);
        }

        public async Task<LlmConnectionTestResult> TestConnectionAsync(DeepseekTheOrcaSettings settings)
        {
            return await TestConnectionAsync(settings, OrcaLlmModelRole.Fallback).ConfigureAwait(false);
        }

        public async Task<LlmConnectionTestResult> TestConnectionAsync(DeepseekTheOrcaSettings settings, OrcaLlmModelRole role)
        {
            if (settings == null)
            {
                return LlmConnectionTestResult.Failure("Settings are unavailable.");
            }

            OrcaLlmRequestConfig config = settings.RequestConfigForRole(role);
            if (config == null)
            {
                return LlmConnectionTestResult.Failure("No model is selected for this role.");
            }

            return await TestConnectionAsync(config.apiKey, config.model, config.baseUrl, config.providerId, config.openAiOrganization, config.openAiProject, config.proxyUrl).ConfigureAwait(false);
        }

        public async Task<OrcaModelDiscoveryResult> ListModelsAsync(OrcaLlmConnectionSettings connection)
        {
            if (connection == null)
            {
                return OrcaModelDiscoveryResult.Failure("Connection settings are unavailable.");
            }

            return await ListModelsAsync(connection.apiKey, connection.ActiveBaseUrl, connection.openAiOrganization, connection.openAiProject, connection.proxyUrl).ConfigureAwait(false);
        }

        public async Task<OrcaModelDiscoveryResult> ListModelsAsync(string apiKey, string baseUrl)
        {
            return await ListModelsAsync(apiKey, baseUrl, "", "").ConfigureAwait(false);
        }

        public async Task<OrcaModelDiscoveryResult> ListModelsAsync(string apiKey, string baseUrl, string openAiOrganization, string openAiProject)
        {
            return await ListModelsAsync(apiKey, baseUrl, openAiOrganization, openAiProject, "").ConfigureAwait(false);
        }

        public async Task<OrcaModelDiscoveryResult> ListModelsAsync(string apiKey, string baseUrl, string openAiOrganization, string openAiProject, string proxyUrl)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return OrcaModelDiscoveryResult.Failure("API key is empty.");
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return OrcaModelDiscoveryResult.Failure("Base URL is empty.");
            }

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            using (HttpClient client = CreateHttpClient(proxyUrl))
            {
                client.Timeout = Timeout;
                client.BaseAddress = new Uri(LlmProviderConfig.NormalizeBaseUrl(baseUrl));
                ApplyAuthorizationHeaders(client, apiKey, openAiOrganization, openAiProject);
                ApplyTransportHeaders(client);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response;
                string responseText;
                try
                {
                    response = await client.GetAsync("models").ConfigureAwait(false);
                    responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return OrcaModelDiscoveryResult.Failure("Connection timed out.");
                }
                catch (Exception ex)
                {
                    return OrcaModelDiscoveryResult.Failure(ex.GetType().Name + ": " + ex.Message);
                }

                if (!response.IsSuccessStatusCode)
                {
                    string error = ExtractErrorMessage(responseText);
                    return OrcaModelDiscoveryResult.Failure(
                        "HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + (string.IsNullOrEmpty(error) ? "" : ": " + error));
                }

                OrcaModelDiscoveryResult result = new OrcaModelDiscoveryResult();
                result.success = true;
                result.models = ParseModelIds(responseText);
                result.message = result.models.Count == 0 ? "Connection succeeded; no models returned." : "Connection succeeded; " + result.models.Count + " model(s) found.";
                return result;
            }
        }

        private async Task<LlmConnectionTestResult> TestConnectionAsync(string apiKey, string model, string baseUrl, string providerId)
        {
            return await TestConnectionAsync(apiKey, model, baseUrl, providerId, "", "").ConfigureAwait(false);
        }

        private async Task<LlmConnectionTestResult> TestConnectionAsync(string apiKey, string model, string baseUrl, string providerId, string openAiOrganization, string openAiProject)
        {
            return await TestConnectionAsync(apiKey, model, baseUrl, providerId, openAiOrganization, openAiProject, "").ConfigureAwait(false);
        }

        private async Task<LlmConnectionTestResult> TestConnectionAsync(string apiKey, string model, string baseUrl, string providerId, string openAiOrganization, string openAiProject, string proxyUrl)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return LlmConnectionTestResult.Failure("API key is empty.");
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                return LlmConnectionTestResult.Failure("Model is empty.");
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return LlmConnectionTestResult.Failure("Base URL is empty.");
            }

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            using (HttpClient client = CreateHttpClient(proxyUrl))
            {
                client.Timeout = Timeout;
                client.BaseAddress = new Uri(LlmProviderConfig.NormalizeBaseUrl(baseUrl));
                ApplyAuthorizationHeaders(client, apiKey, openAiOrganization, openAiProject);
                ApplyTransportHeaders(client);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                string body = BuildConnectionTestBody(model.Trim(), providerId);
                using (StringContent content = new StringContent(body, Encoding.UTF8, "application/json"))
                {
                    HttpResponseMessage response;
                    string responseText;
                    try
                    {
                        response = await client.PostAsync("chat/completions", content).ConfigureAwait(false);
                        responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        return LlmConnectionTestResult.Failure("Connection timed out.");
                    }
                    catch (Exception ex)
                    {
                        return LlmConnectionTestResult.Failure(ex.GetType().Name + ": " + ex.Message);
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        return LlmConnectionTestResult.Success("Connection succeeded.");
                    }

                    string error = ExtractErrorMessage(responseText);
                    return LlmConnectionTestResult.Failure(
                        "HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + (string.IsNullOrEmpty(error) ? "" : ": " + error));
                }
            }
        }

        private static string BuildConnectionTestBody(string model, string providerId)
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = model;
            body["messages"] = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "role", "user" },
                    { "content", "connection test; reply ok" }
                }
            };
            body[UsesOpenAiChatCompletionTokens(providerId) ? "max_completion_tokens" : "max_tokens"] = 8;
            body["stream"] = false;
            return MiniJson.Serialize(body);
        }

        public async Task<LlmChatResponse> SendChatCompletionAsync(string apiKey, string model, List<LlmChatMessage> messages)
        {
            return await SendChatCompletionAsync(apiKey, model, messages, includeTools: true, maxTokens: 512, temperature: 0.7f).ConfigureAwait(false);
        }

        public async Task<LlmChatResponse> SendChatCompletionAsync(DeepseekTheOrcaSettings settings, List<LlmChatMessage> messages)
        {
            return await SendChatCompletionAsync(settings, messages, includeTools: true, maxTokens: 512, temperature: 0.7f, role: OrcaLlmModelRole.Fallback).ConfigureAwait(false);
        }

        public async Task<LlmChatResponse> SendChatCompletionAsync(DeepseekTheOrcaSettings settings, List<LlmChatMessage> messages, OrcaLlmModelRole role)
        {
            return await SendChatCompletionAsync(settings, messages, includeTools: true, maxTokens: 512, temperature: 0.7f, role: role).ConfigureAwait(false);
        }

        public async Task<LlmChatResponse> SendPlainChatCompletionAsync(string apiKey, string model, List<LlmChatMessage> messages)
        {
            return await SendChatCompletionAsync(apiKey, model, messages, includeTools: false, maxTokens: 800, temperature: 0.85f).ConfigureAwait(false);
        }

        public async Task<LlmChatResponse> SendPlainChatCompletionAsync(DeepseekTheOrcaSettings settings, List<LlmChatMessage> messages)
        {
            return await SendPlainChatCompletionAsync(settings, messages, OrcaLlmModelRole.Fallback).ConfigureAwait(false);
        }

        public async Task<LlmChatResponse> SendPlainChatCompletionAsync(DeepseekTheOrcaSettings settings, List<LlmChatMessage> messages, OrcaLlmModelRole role)
        {
            return await SendChatCompletionAsync(settings, messages, includeTools: false, maxTokens: 800, temperature: 0.85f, role: role).ConfigureAwait(false);
        }

        public LlmStreamingChatRequest StartStreamingPlainChatCompletion(DeepseekTheOrcaSettings settings, List<LlmChatMessage> messages, int maxTokens, float temperature, OrcaLlmModelRole role)
        {
            LlmStreamingChatRequest request = new LlmStreamingChatRequest();
            if (settings == null)
            {
                request.Fail("Settings are unavailable.");
                return request;
            }

            OrcaLlmRequestConfig config = settings.RequestConfigForRole(role);
            if (config == null)
            {
                request.Fail("No model is selected for this role.");
                return request;
            }

            request.role = role.ToString();
            request.model = config.model == null ? "" : config.model.Trim();
            request.providerId = config.providerId;
            Task.Run(async delegate
            {
                await SendStreamingChatCompletionAsync(
                    request,
                    config.apiKey,
                    config.model,
                    config.baseUrl,
                    config.IncludeThinkingToggle,
                    messages,
                    maxTokens,
                    temperature,
                    config.providerId,
                    config.openAiOrganization,
                    config.openAiProject,
                    config.proxyUrl,
                    role).ConfigureAwait(false);
            });

            return request;
        }

        public async Task<LlmChatResponse> SendChatCompletionWithToolsAsync(string apiKey, string model, List<LlmChatMessage> messages, List<Dictionary<string, object>> tools, int maxTokens, float temperature)
        {
            return await SendChatCompletionAsync(apiKey, model, messages, tools, maxTokens, temperature).ConfigureAwait(false);
        }

        public async Task<LlmChatResponse> SendChatCompletionWithToolsAsync(DeepseekTheOrcaSettings settings, List<LlmChatMessage> messages, List<Dictionary<string, object>> tools, int maxTokens, float temperature)
        {
            return await SendChatCompletionWithToolsAsync(settings, messages, tools, maxTokens, temperature, OrcaLlmModelRole.Fallback).ConfigureAwait(false);
        }

        public async Task<LlmChatResponse> SendChatCompletionWithToolsAsync(DeepseekTheOrcaSettings settings, List<LlmChatMessage> messages, List<Dictionary<string, object>> tools, int maxTokens, float temperature, OrcaLlmModelRole role)
        {
            if (settings == null)
            {
                return LlmChatResponse.Failure("Settings are unavailable.");
            }

            OrcaLlmRequestConfig config = settings.RequestConfigForRole(role);
            if (config == null)
            {
                return LlmChatResponse.Failure("No model is selected for this role.");
            }

            return await SendChatCompletionAsync(config.apiKey, config.model, config.baseUrl, config.IncludeThinkingToggle, messages, tools, maxTokens, temperature, config.providerId, config.openAiOrganization, config.openAiProject, config.proxyUrl, role).ConfigureAwait(false);
        }

        private async Task<LlmChatResponse> SendChatCompletionAsync(string apiKey, string model, List<LlmChatMessage> messages, bool includeTools, int maxTokens, float temperature)
        {
            return await SendChatCompletionAsync(apiKey, model, messages, includeTools ? LlmToolSchemas.Build() : null, maxTokens, temperature).ConfigureAwait(false);
        }

        private async Task<LlmChatResponse> SendChatCompletionAsync(DeepseekTheOrcaSettings settings, List<LlmChatMessage> messages, bool includeTools, int maxTokens, float temperature, OrcaLlmModelRole role)
        {
            if (settings == null)
            {
                return LlmChatResponse.Failure("Settings are unavailable.");
            }

            OrcaLlmRequestConfig config = settings.RequestConfigForRole(role);
            if (config == null)
            {
                return LlmChatResponse.Failure("No model is selected for this role.");
            }

            return await SendChatCompletionAsync(config.apiKey, config.model, config.baseUrl, config.IncludeThinkingToggle, messages, includeTools ? LlmToolSchemas.Build() : null, maxTokens, temperature, config.providerId, config.openAiOrganization, config.openAiProject, config.proxyUrl, role).ConfigureAwait(false);
        }

        private async Task<LlmChatResponse> SendChatCompletionAsync(string apiKey, string model, List<LlmChatMessage> messages, List<Dictionary<string, object>> tools, int maxTokens, float temperature)
        {
            return await SendChatCompletionAsync(apiKey, model, LlmProviderConfig.Profile(LlmProviderConfig.DeepSeek).defaultBaseUrl, true, messages, tools, maxTokens, temperature, LlmProviderConfig.DeepSeek).ConfigureAwait(false);
        }

        private async Task<LlmChatResponse> SendChatCompletionAsync(string apiKey, string model, string baseUrl, bool includeThinkingToggle, List<LlmChatMessage> messages, List<Dictionary<string, object>> tools, int maxTokens, float temperature)
        {
            return await SendChatCompletionAsync(apiKey, model, baseUrl, includeThinkingToggle, messages, tools, maxTokens, temperature, LlmProviderConfig.DeepSeek).ConfigureAwait(false);
        }

        private async Task<LlmChatResponse> SendChatCompletionAsync(string apiKey, string model, string baseUrl, bool includeThinkingToggle, List<LlmChatMessage> messages, List<Dictionary<string, object>> tools, int maxTokens, float temperature, string providerId)
        {
            return await SendChatCompletionAsync(apiKey, model, baseUrl, includeThinkingToggle, messages, tools, maxTokens, temperature, providerId, "", "").ConfigureAwait(false);
        }

        private async Task<LlmChatResponse> SendChatCompletionAsync(string apiKey, string model, string baseUrl, bool includeThinkingToggle, List<LlmChatMessage> messages, List<Dictionary<string, object>> tools, int maxTokens, float temperature, string providerId, string openAiOrganization, string openAiProject)
        {
            return await SendChatCompletionAsync(apiKey, model, baseUrl, includeThinkingToggle, messages, tools, maxTokens, temperature, providerId, openAiOrganization, openAiProject, "").ConfigureAwait(false);
        }

        private async Task<LlmChatResponse> SendChatCompletionAsync(string apiKey, string model, string baseUrl, bool includeThinkingToggle, List<LlmChatMessage> messages, List<Dictionary<string, object>> tools, int maxTokens, float temperature, string providerId, string openAiOrganization, string openAiProject, string proxyUrl)
        {
            return await SendChatCompletionAsync(apiKey, model, baseUrl, includeThinkingToggle, messages, tools, maxTokens, temperature, providerId, openAiOrganization, openAiProject, proxyUrl, OrcaLlmModelRole.Fallback).ConfigureAwait(false);
        }

        private async Task<LlmChatResponse> SendChatCompletionAsync(string apiKey, string model, string baseUrl, bool includeThinkingToggle, List<LlmChatMessage> messages, List<Dictionary<string, object>> tools, int maxTokens, float temperature, string providerId, string openAiOrganization, string openAiProject, string proxyUrl, OrcaLlmModelRole role)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return LlmChatResponse.Failure("API key is empty.");
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                return LlmChatResponse.Failure("Model is empty.");
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return LlmChatResponse.Failure("Base URL is empty.");
            }

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            for (int attempt = 1; attempt <= MaxTransportAttempts; attempt++)
            {
                using (HttpClient client = CreateHttpClient(proxyUrl))
                {
                    client.Timeout = Timeout;
                    client.BaseAddress = new Uri(LlmProviderConfig.NormalizeBaseUrl(baseUrl));
                    ApplyAuthorizationHeaders(client, apiKey, openAiOrganization, openAiProject);
                    ApplyTransportHeaders(client);
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    string body = BuildChatCompletionBody(model.Trim(), messages, tools, maxTokens, temperature, includeThinkingToggle, providerId);
                    using (StringContent content = new StringContent(body, Encoding.UTF8, "application/json"))
                    {
                        HttpResponseMessage response;
                        string responseText;
                        Stopwatch stopwatch = Stopwatch.StartNew();
                        try
                        {
                            response = await client.PostAsync("chat/completions", content).ConfigureAwait(false);
                            responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }
                        catch (TaskCanceledException)
                        {
                            LlmConnectionTester.ReportFailedCall("Connection timed out.");
                            return LlmChatResponse.Failure("Connection timed out.");
                        }
                        catch (Exception ex)
                        {
                            stopwatch.Stop();
                            if (IsTransientTransportException(ex) && attempt < MaxTransportAttempts)
                            {
                                LlmConnectionTester.ReportFailedCall(TransientRetryMessage(ex, attempt));
                                await Task.Delay(250).ConfigureAwait(false);
                                continue;
                            }

                            string failure = TransportFailureMessage(ex);
                            LlmConnectionTester.ReportFailedCall(failure);
                            return LlmChatResponse.Failure(failure);
                        }
                        finally
                        {
                            stopwatch.Stop();
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            string error = ExtractErrorMessage(responseText);
                            LlmConnectionTester.ReportFailedCall(
                                "HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + (string.IsNullOrEmpty(error) ? "" : ": " + error));
                            return LlmChatResponse.Failure(
                                "HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + (string.IsNullOrEmpty(error) ? "" : ": " + error));
                        }

                        LlmChatResponse parsed = ParseChatResponse(responseText);
                        parsed.elapsedMs = (int)stopwatch.ElapsedMilliseconds;
                        parsed.role = role.ToString();
                        parsed.model = model.Trim();
                        parsed.providerId = providerId;
                        if (parsed.success)
                        {
                            LlmConnectionTester.ReportSuccessfulCall("Connection succeeded by chat completion.");
                            LlmUsageTracker.Record(parsed);
                        }

                        return parsed;
                    }
                }
            }

            return LlmChatResponse.Failure("Transport failed after retry.");
        }

        private async Task SendStreamingChatCompletionAsync(
            LlmStreamingChatRequest streamingRequest,
            string apiKey,
            string model,
            string baseUrl,
            bool includeThinkingToggle,
            List<LlmChatMessage> messages,
            int maxTokens,
            float temperature,
            string providerId,
            string openAiOrganization,
            string openAiProject,
            string proxyUrl,
            OrcaLlmModelRole role)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                if (streamingRequest == null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    streamingRequest.Fail("API key is empty.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(model))
                {
                    streamingRequest.Fail("Model is empty.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    streamingRequest.Fail("Base URL is empty.");
                    return;
                }

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                for (int attempt = 1; attempt <= MaxTransportAttempts; attempt++)
                {
                    using (HttpClient client = CreateHttpClient(proxyUrl))
                    {
                        client.Timeout = StreamingTimeout;
                        client.BaseAddress = new Uri(LlmProviderConfig.NormalizeBaseUrl(baseUrl));
                        ApplyAuthorizationHeaders(client, apiKey, openAiOrganization, openAiProject);
                        ApplyTransportHeaders(client);
                        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                        string body = BuildChatCompletionBody(model.Trim(), messages, null, maxTokens, temperature, includeThinkingToggle, providerId, stream: true);
                        using (StringContent content = new StringContent(body, Encoding.UTF8, "application/json"))
                        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "chat/completions"))
                        {
                            request.Content = content;
                            HttpResponseMessage response;
                            try
                            {
                                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                            }
                            catch (TaskCanceledException)
                            {
                                streamingRequest.Fail("Connection timed out.");
                                LlmConnectionTester.ReportFailedCall("Connection timed out.");
                                return;
                            }
                            catch (Exception ex)
                            {
                                if (IsTransientTransportException(ex) && attempt < MaxTransportAttempts)
                                {
                                    LlmConnectionTester.ReportFailedCall(TransientRetryMessage(ex, attempt));
                                    await Task.Delay(250).ConfigureAwait(false);
                                    continue;
                                }

                                string failure = TransportFailureMessage(ex);
                                streamingRequest.Fail(failure);
                                LlmConnectionTester.ReportFailedCall(failure);
                                return;
                            }

                            using (response)
                            {
                                if (!response.IsSuccessStatusCode)
                                {
                                    string responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                    string error = ExtractErrorMessage(responseText);
                                    string message = "HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + (string.IsNullOrEmpty(error) ? "" : ": " + error);
                                    streamingRequest.Fail(message);
                                    LlmConnectionTester.ReportFailedCall(message);
                                    return;
                                }

                                using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                                {
                                    string line;
                                    while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                                    {
                                        if (streamingRequest.IsCancellationRequested)
                                        {
                                            streamingRequest.Fail("Streaming request cancelled.");
                                            return;
                                        }

                                        if (!line.StartsWith("data:", StringComparison.Ordinal))
                                        {
                                            continue;
                                        }

                                        string data = line.Substring(5).Trim();
                                        if (data == "[DONE]")
                                        {
                                            break;
                                        }

                                        ParseStreamingChunk(data, streamingRequest);
                                    }
                                }
                            }

                            break;
                        }
                    }
                }

                stopwatch.Stop();
                LlmChatResponse parsed = LlmChatResponse.Success();
                parsed.content = streamingRequest.RawContent;
                parsed.elapsedMs = (int)stopwatch.ElapsedMilliseconds;
                parsed.role = role.ToString();
                parsed.model = model.Trim();
                parsed.providerId = providerId;
                streamingRequest.Complete(parsed);
                LlmConnectionTester.ReportSuccessfulCall("Connection succeeded by streaming chat completion.");
                LlmUsageTracker.Record(parsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                if (streamingRequest != null)
                {
                    streamingRequest.Fail(ex.GetType().Name + ": " + ex.Message);
                }
                LlmConnectionTester.ReportFailedCall(ex.GetType().Name + ": " + ex.Message);
            }
        }

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

        private static LlmChatResponse ParseChatResponse(string responseText)
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

                if (parsed.toolCalls.Count == 0 && !string.IsNullOrEmpty(parsed.content))
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

    public sealed class LlmChatMessage
    {
        public string role;
        public string content;
        public string toolCallId;
        public List<LlmToolCall> toolCalls;

        public static LlmChatMessage System(string content)
        {
            return new LlmChatMessage { role = "system", content = content };
        }

        public static LlmChatMessage User(string content)
        {
            return new LlmChatMessage { role = "user", content = content };
        }

        public static LlmChatMessage Assistant(string content, List<LlmToolCall> toolCalls)
        {
            return new LlmChatMessage { role = "assistant", content = content, toolCalls = toolCalls };
        }

        public static LlmChatMessage Tool(string toolCallId, string content)
        {
            return new LlmChatMessage { role = "tool", toolCallId = toolCallId, content = content };
        }
    }

    public sealed class LlmToolCall
    {
        public string id;
        public string name;
        public string argumentsJson;
    }

    public sealed class LlmChatResponse
    {
        public bool success;
        public string errorMessage;
        public string content;
        public int promptTokens;
        public int completionTokens;
        public int totalTokens;
        public int cacheHitTokens;
        public int cacheMissTokens;
        public int elapsedMs;
        public string role;
        public string model;
        public string providerId;
        public readonly List<LlmToolCall> toolCalls = new List<LlmToolCall>();

        public static LlmChatResponse Success()
        {
            return new LlmChatResponse { success = true };
        }

        public static LlmChatResponse Failure(string message)
        {
            return new LlmChatResponse { success = false, errorMessage = message };
        }
    }

    public sealed class LlmStreamingChatRequest
    {
        private readonly object syncRoot = new object();
        private readonly StringBuilder rawContent = new StringBuilder();
        private readonly JsonReplyStreamExtractor replyExtractor = new JsonReplyStreamExtractor();
        private bool completed;
        private bool cancelled;
        private LlmChatResponse finalResponse;
        private string errorMessage = "";
        private string visibleText = "";
        public string role = "";
        public string model = "";
        public string providerId = "";

        public bool IsCompleted
        {
            get
            {
                lock (syncRoot)
                {
                    return completed;
                }
            }
        }

        public bool IsCancellationRequested
        {
            get
            {
                lock (syncRoot)
                {
                    return cancelled;
                }
            }
        }

        public bool Success
        {
            get
            {
                lock (syncRoot)
                {
                    return completed && finalResponse != null && finalResponse.success;
                }
            }
        }

        public string ErrorMessage
        {
            get
            {
                lock (syncRoot)
                {
                    return errorMessage;
                }
            }
        }

        public string VisibleText
        {
            get
            {
                lock (syncRoot)
                {
                    return visibleText;
                }
            }
        }

        public string RawContent
        {
            get
            {
                lock (syncRoot)
                {
                    return rawContent.ToString();
                }
            }
        }

        public LlmChatResponse FinalResponse
        {
            get
            {
                lock (syncRoot)
                {
                    return finalResponse;
                }
            }
        }

        public void AppendContent(string delta)
        {
            if (string.IsNullOrEmpty(delta))
            {
                return;
            }

            lock (syncRoot)
            {
                if (completed || cancelled)
                {
                    return;
                }

                rawContent.Append(delta);
                visibleText = JsonReplyStreamExtractor.SanitizeVisibleText(replyExtractor.Extract(rawContent.ToString()));
            }
        }

        public void Complete(LlmChatResponse response)
        {
            lock (syncRoot)
            {
                if (completed)
                {
                    return;
                }

                if (response == null)
                {
                    response = LlmChatResponse.Failure("Streaming response was empty.");
                }

                if (response.success && !string.IsNullOrEmpty(response.content))
                {
                    List<LlmToolCall> dsmlToolCalls = OrcaDsmlToolCallFallbackParser.ParseToolCalls(response.content);
                    if (dsmlToolCalls.Count > 0)
                    {
                        response.toolCalls.AddRange(dsmlToolCalls);
                        response.content = OrcaDsmlToolCallFallbackParser.StripToolCalls(response.content);
                    }
                }

                finalResponse = response;
                if (!response.success)
                {
                    errorMessage = response.errorMessage ?? "";
                }
                visibleText = JsonReplyStreamExtractor.SanitizeVisibleText(replyExtractor.Extract(rawContent.ToString()));
                completed = true;
            }
        }

        public void Fail(string message)
        {
            lock (syncRoot)
            {
                if (completed)
                {
                    return;
                }

                errorMessage = message ?? "";
                finalResponse = LlmChatResponse.Failure(errorMessage);
                finalResponse.content = rawContent.ToString();
                finalResponse.role = role;
                finalResponse.model = model;
                finalResponse.providerId = providerId;
                visibleText = JsonReplyStreamExtractor.SanitizeVisibleText(replyExtractor.Extract(rawContent.ToString()));
                completed = true;
            }
        }

        public void Cancel()
        {
            lock (syncRoot)
            {
                cancelled = true;
            }
        }
    }

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

    public static class LlmToolSchemas
    {
        public static List<Dictionary<string, object>> Build()
        {
            List<Dictionary<string, object>> tools = new List<Dictionary<string, object>>();
            foreach (AiToolDefinition definition in AiStoryToolRegistry.StorytellerPlanningDefinitions)
            {
                tools.Add(Function(definition.Name, definition.Description, definition.parameters ?? EmptyParameters()));
            }
            return tools;
        }

        public static List<Dictionary<string, object>> BuildChatTools()
        {
            List<Dictionary<string, object>> tools = new List<Dictionary<string, object>>();
            foreach (AiToolDefinition definition in AiStoryToolRegistry.ChatDefinitions)
            {
                if (definition.Name == "web_search" && (DeepseekTheOrcaMod.Settings == null || !DeepseekTheOrcaMod.Settings.UsesLocalWebSearchTool))
                {
                    continue;
                }

                tools.Add(Function(definition.Name, definition.Description, definition.parameters ?? EmptyParameters()));
            }
            AppendHttpMcpTools(tools);
            return tools;
        }

        private static void AppendHttpMcpTools(List<Dictionary<string, object>> tools)
        {
            List<OrcaMcpToolDescriptor> mcpTools = OrcaHttpMcpClient.DiscoverTools();
            for (int i = 0; i < mcpTools.Count; i++)
            {
                OrcaMcpToolDescriptor tool = mcpTools[i];
                tools.Add(Function(tool.exposedName, tool.description, tool.inputSchema ?? EmptyParameters()));
            }
        }

        private static Dictionary<string, object> Function(string name, string description, Dictionary<string, object> parameters)
        {
            return new Dictionary<string, object>
            {
                { "type", "function" },
                { "function", new Dictionary<string, object>
                    {
                        { "name", name },
                        { "description", description },
                        { "parameters", parameters }
                    }
                }
            };
        }

        public static Dictionary<string, object> EmptyParameters()
        {
            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>() }
            };
        }

        public static Dictionary<string, object> CountParameters()
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            properties["count"] = new Dictionary<string, object>
            {
                { "type", "integer" },
                { "description", "Maximum number of recent letters to return. Range 1 to 10. Defaults to 5." }
            };

            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", properties }
            };
        }

        public static Dictionary<string, object> RimtalkChatHistoryParameters()
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            properties["count"] = new Dictionary<string, object>
            {
                { "type", "integer" },
                { "description", "Maximum number of recent RimTalk records to return. Range 1 to 30. Defaults to 10." }
            };
            properties["origin"] = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "Optional filter: all, player_initiated, or ai_auto_generated. Defaults to all." }
            };
            properties["maxChars"] = new Dictionary<string, object>
            {
                { "type", "integer" },
                { "description", "Maximum characters per prompt or response field. Range 80 to 2000. Defaults to 500." }
            };

            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", properties }
            };
        }

        public static Dictionary<string, object> MapPawnListParameters()
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            properties["filter"] = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "Optional pawn filter: all, player, colonist, free_colonist, humanlike, animal, hostile, prisoner, or slave. Defaults to all." }
            };
            properties["count"] = new Dictionary<string, object>
            {
                { "type", "integer" },
                { "description", "Maximum number of pawns to return. Range 1 to 100. Defaults to 50." }
            };

            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", properties }
            };
        }

        public static Dictionary<string, object> PawnDetailsParameters()
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            properties["pawnId"] = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "Pawn id returned by list_map_pawns. A pawn name or ThingID may also work as fallback." }
            };

            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", properties },
                { "required", new List<object> { "pawnId" } }
            };
        }

        public static Dictionary<string, object> RaidParameters()
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            properties["factionDef"] = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "Optional hostile faction defName or faction name, such as Pirate or Mechanoid. Required when specifying strategy, arrival mode, or spawn cell. Omit only to let RimWorld choose an unconstrained raid." }
            };
            properties["raidStrategyDef"] = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "Optional RaidStrategyDef defName, for example ImmediateAttack, ImmediateAttackSmart, ImmediateAttackSappers, ImmediateAttackBreaching, or ImmediateAttackBreachingSmart." }
            };
            properties["raidArrivalModeDef"] = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "Optional PawnsArrivalModeDef defName, for example EdgeWalkIn, EdgeDrop, CenterDrop, RandomDrop, or SpecificDropDebug. Use SpecificDropDebug for a specified spawnCell." }
            };
            properties["spawnCell"] = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "Optional map cell as x,z or x,y,z. Requires raidArrivalModeDef SpecificDropDebug; if omitted while spawnCell is set, SpecificDropDebug is assumed." }
            };
            properties["pointsFactor"] = new Dictionary<string, object>
            {
                { "type", "number" },
                { "description", "Threat point multiplier for the raid. Defaults to 1.0." }
            };
            properties["reason"] = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "Short reason for the raid story beat." }
            };

            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", properties }
            };
        }

        public static Dictionary<string, object> SpawnPawnsParameters()
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            properties["factionDef"] = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "Faction defName or faction name, for example Pirate, OutlanderCivil, TribeRough, or Mechanoid." }
            };
            properties["count"] = new Dictionary<string, object>
            {
                { "type", "integer" },
                { "description", "Number of pawns to spawn. Range 1 to 50." }
            };
            properties["spawnCell"] = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "Target map cell as x,z or x,y,z." }
            };
            properties["radius"] = new Dictionary<string, object>
            {
                { "type", "integer" },
                { "description", "Optional placement radius around spawnCell. Range 0 to 30. Defaults to 5." }
            };
            properties["reason"] = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "Short reason for the pawn spawn request." }
            };

            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", properties },
                { "required", new List<object> { "factionDef", "count", "spawnCell" } }
            };
        }

        public static Dictionary<string, object> WebSearchParameters()
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            properties["query"] = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "Search query." }
            };
            properties["maxResults"] = new Dictionary<string, object>
            {
                { "type", "integer" },
                { "description", "Maximum concise results to return. Range 1 to the configured Tavily cap. Defaults to the configured cap." }
            };
            properties["topic"] = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "Optional Tavily topic: general, news, or finance. Defaults to general." }
            };
            properties["timeRange"] = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "Optional recency filter: day, week, month, year, d, w, m, or y." }
            };

            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", properties },
                { "required", new List<object> { "query" } }
            };
        }

        public static Dictionary<string, object> IncidentDefParameters(bool includeReason, bool includePointsFactor)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            properties["incidentDef"] = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "RimWorld IncidentDef defName from list_available_incidents." }
            };

            if (includePointsFactor)
            {
                properties["pointsFactor"] = new Dictionary<string, object>
                {
                    { "type", "number" },
                    { "description", "Threat point multiplier. Keep near 1.0 unless the story strongly justifies it." }
                };
            }

            if (includeReason)
            {
                properties["reason"] = new Dictionary<string, object>
                {
                    { "type", "string" },
                    { "description", "Short reason for the chosen story beat." }
                };
            }

            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", properties },
                { "required", new List<object> { "incidentDef" } }
            };
        }
    }
}
