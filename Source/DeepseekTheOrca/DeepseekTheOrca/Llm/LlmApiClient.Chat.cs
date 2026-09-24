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
using System.Threading;
namespace DeepseekTheOrca
{
    public sealed partial class LlmApiClient
    {
        // A captured config, no tools, one transport attempt, and low-priority admission.
        public Task<LlmChatResponse> SendBackgroundCompletionAsync(OrcaLlmRequestConfig config, List<LlmChatMessage> messages, int maxTokens, CancellationToken cancellation)
        {
            return SendBackgroundCompletionAsync(config, messages, maxTokens, cancellation, OrcaLlmModelRole.Dialogue, 0.8f);
        }

        public Task<LlmChatResponse> SendBackgroundCompletionAsync(OrcaLlmRequestConfig config, List<LlmChatMessage> messages, int maxTokens,
            CancellationToken cancellation, OrcaLlmModelRole role, float temperature)
        {
            if (config == null) return Task.FromResult(LlmChatResponse.Failure("No model is configured for " + role + "."));
            return SendChatCompletionAsync(config.apiKey, config.model, config.baseUrl, config.IncludeThinkingToggle,
                messages, null, maxTokens, temperature, config.providerId, config.openAiOrganization, config.openAiProject,
                config.proxyUrl, role, true, cancellation);
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
            request.allowDsmlToolCallFallback = AllowDsmlToolCallFallback(role);
            CancellationToken capturedLifetime = queueLifetime.Token;
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
                    role, capturedLifetime).ConfigureAwait(false);
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

        public async Task<OrcaEmbeddingResult> SendEmbeddingAsync(DeepseekTheOrcaSettings settings, string text)
        {
            return await SendEmbeddingAsync(settings, text, (int)EmbeddingTimeout.TotalMilliseconds).ConfigureAwait(false);
        }

        public async Task<OrcaEmbeddingResult> SendEmbeddingAsync(DeepseekTheOrcaSettings settings, string text, int timeoutMs)
        {
            if (settings == null)
            {
                return OrcaEmbeddingResult.Failure("Settings are unavailable.");
            }

            OrcaLlmRequestConfig config = settings.RequestConfigForRole(OrcaLlmModelRole.Embedding);
            if (config == null)
            {
                return OrcaEmbeddingResult.Failure("No embedding model is selected.");
            }

            var result = await SendEmbeddingAsync(config.apiKey, config.model, config.baseUrl, text, config.providerId, config.openAiOrganization, config.openAiProject, config.proxyUrl, timeoutMs).ConfigureAwait(false);
            if (result != null) result.identity = OrcaEmbeddingIdentity.For(config);
            return result;
        }

        private async Task<OrcaEmbeddingResult> SendEmbeddingAsync(string apiKey, string model, string baseUrl, string text, string providerId, string openAiOrganization, string openAiProject, string proxyUrl)
        {
            return await SendEmbeddingAsync(apiKey, model, baseUrl, text, providerId, openAiOrganization, openAiProject, proxyUrl, (int)EmbeddingTimeout.TotalMilliseconds).ConfigureAwait(false);
        }

        private async Task<OrcaEmbeddingResult> SendEmbeddingAsync(string apiKey, string model, string baseUrl, string text, string providerId, string openAiOrganization, string openAiProject, string proxyUrl, int timeoutMs)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return OrcaEmbeddingResult.Failure("API key is empty.");
            }
            if (string.IsNullOrWhiteSpace(model))
            {
                return OrcaEmbeddingResult.Failure("Model is empty.");
            }
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return OrcaEmbeddingResult.Failure("Base URL is empty.");
            }

            CancellationToken capturedLifetime = queueLifetime.Token;
            return await LlmRequestScheduler.RunAsync("embedding", async delegate
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (HttpClient client = CreateHttpClient(proxyUrl))
                {
                    client.Timeout = TimeSpan.FromMilliseconds(Math.Max(1000, timeoutMs));
                    client.BaseAddress = new Uri(LlmProviderConfig.NormalizeBaseUrl(baseUrl));
                    ApplyAuthorizationHeaders(client, apiKey, openAiOrganization, openAiProject);
                    ApplyTransportHeaders(client);
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    Dictionary<string, object> body = new Dictionary<string, object>();
                    body["model"] = model.Trim();
                    body["input"] = text ?? "";
                    using (StringContent content = new StringContent(MiniJson.Serialize(body), Encoding.UTF8, "application/json"))
                    {
                        HttpResponseMessage response;
                        string responseText;
                        try
                        {
                            response = await client.PostAsync("embeddings", content).ConfigureAwait(false);
                            responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            return OrcaEmbeddingResult.Failure(TransportFailureMessage(ex));
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            string error = ExtractErrorMessage(responseText);
                            return OrcaEmbeddingResult.Failure("HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + (string.IsNullOrEmpty(error) ? "" : ": " + error));
                        }

                        return ParseEmbeddingResponse(responseText);
                    }
                }
            }, false, capturedLifetime).ConfigureAwait(false);
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

            return await SendChatCompletionAsync(config.apiKey, config.model, config.baseUrl, config.IncludeThinkingToggle, messages, includeTools ? LlmToolSchemas.BuildForRole(role) : null, maxTokens, temperature, config.providerId, config.openAiOrganization, config.openAiProject, config.proxyUrl, role).ConfigureAwait(false);
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

        private async Task<LlmChatResponse> SendChatCompletionAsync(string apiKey, string model, string baseUrl, bool includeThinkingToggle, List<LlmChatMessage> messages, List<Dictionary<string, object>> tools, int maxTokens, float temperature, string providerId, string openAiOrganization, string openAiProject, string proxyUrl, OrcaLlmModelRole role, bool background = false, CancellationToken cancellation = default(CancellationToken))
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

            int attempts = background ? 1 : MaxTransportAttempts;
            if (!cancellation.CanBeCanceled) cancellation = queueLifetime.Token;
            return await LlmRequestScheduler.RunAsync((background ? "background completion " : "chat completion ") + role, async delegate
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                for (int attempt = 1; attempt <= attempts; attempt++)
                {
                    if (attempt > 1) cancellation.ThrowIfCancellationRequested();
                    using (HttpClient client = CreateHttpClient(proxyUrl))
                    {
                        client.Timeout = ChatTimeoutForRole(role);
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
                                if (attempt < attempts)
                                {
                                    LlmConnectionTester.ReportFailedCall("Connection timed out on attempt " + attempt + "; retrying once.");
                                    await Task.Delay(250).ConfigureAwait(false);
                                    continue;
                                }

                                LlmConnectionTester.ReportFailedCall("Connection timed out.");
                                return LlmChatResponse.Failure("Connection timed out.");
                            }
                            catch (Exception ex)
                            {
                                stopwatch.Stop();
                                if (IsTransientTransportException(ex) && attempt < attempts)
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

                            LlmChatResponse parsed = ParseChatResponse(responseText, role);
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
            }, background, cancellation).ConfigureAwait(false);
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
            OrcaLlmModelRole role, CancellationToken cancellation)
        {
            Stopwatch stopwatch = null;
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

                await LlmRequestScheduler.RunAsync("streaming chat completion " + role, async delegate
                {
                    streamingRequest.CancellationToken.ThrowIfCancellationRequested();
                    streamingRequest.SetStage("admitted");
                    stopwatch = Stopwatch.StartNew();
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                    for (int attempt = 1; attempt <= MaxTransportAttempts; attempt++)
                    {
                        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(streamingRequest.CancellationToken))
                        using (HttpClient client = CreateHttpClient(proxyUrl))
                        {
                            deadline.CancelAfter(StreamingTimeout);
                            streamingRequest.SetStage("sending attempt " + attempt);
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
                                    response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException)
                                {
                                    streamingRequest.Fail(streamingRequest.IsCancellationRequested ? "Streaming request cancelled." : "Streaming request timed out before response headers.");
                                    LlmConnectionTester.ReportFailedCall("Connection timed out.");
                                    return;
                                }
                                catch (Exception ex)
                                {
                                    if (IsTransientTransportException(ex) && attempt < MaxTransportAttempts)
                                    {
                                        LlmConnectionTester.ReportFailedCall(TransientRetryMessage(ex, attempt));
                                        await Task.Delay(250, deadline.Token).ConfigureAwait(false);
                                        continue;
                                    }

                                    string failure = TransportFailureMessage(ex);
                                    streamingRequest.Fail(failure);
                                    LlmConnectionTester.ReportFailedCall(failure);
                                    return;
                                }

                                using (response)
                                {
                                    streamingRequest.SetStage("headers HTTP " + (int)response.StatusCode + "; contentType=" + response.Content.Headers.ContentType);
                                    if (!response.IsSuccessStatusCode)
                                    {
                                        string responseText = await LlmStreamDeadline.Await(response.Content.ReadAsStringAsync(), deadline.Token).ConfigureAwait(false);
                                        string error = ExtractErrorMessage(responseText);
                                        string message = "HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + (string.IsNullOrEmpty(error) ? "" : ": " + error);
                                        streamingRequest.Fail(message);
                                        LlmConnectionTester.ReportFailedCall(message);
                                        return;
                                    }

                                    using (Stream stream = await LlmStreamDeadline.Await(response.Content.ReadAsStreamAsync(), deadline.Token).ConfigureAwait(false))
                                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                                    {
                                        streamingRequest.SetStage("reading body");
                                        string line;
                                        while ((line = await LlmStreamDeadline.Await(reader.ReadLineAsync(), deadline.Token).ConfigureAwait(false)) != null)
                                        {
                                            streamingRequest.ReceivedLine();
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

                                            if (data.Length != 0) ParseStreamingChunk(data, streamingRequest);
                                        }
                                    }
                                }

                                break;
                            }
                        }
                    }

                    stopwatch.Stop();
                    if (string.IsNullOrWhiteSpace(streamingRequest.RawContent))
                        throw new InvalidDataException("Stream ended without reply content.");
                    if (streamingRequest.FinishReason == "length" || streamingRequest.FinishReason == "content_filter")
                        throw new InvalidDataException("Stream ended with finish_reason=" + streamingRequest.FinishReason);
                    LlmChatResponse parsed = LlmChatResponse.Success();
                    parsed.content = streamingRequest.RawContent;
                    parsed.elapsedMs = (int)stopwatch.ElapsedMilliseconds;
                    parsed.role = role.ToString();
                    parsed.model = model.Trim();
                    parsed.providerId = providerId;
                    streamingRequest.Complete(parsed);
                    LlmConnectionTester.ReportSuccessfulCall("Connection succeeded by streaming chat completion.");
                    LlmUsageTracker.Record(parsed);
                }, false, cancellation).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (stopwatch != null)
                {
                    stopwatch.Stop();
                }
                if (streamingRequest != null)
                {
                    string detail = ex is OperationCanceledException
                        ? (streamingRequest.IsCancellationRequested ? "Streaming request cancelled." : "Streaming request exceeded its timeout while queued or receiving the response.")
                        : ex.GetType().Name + ": " + ex.Message;
                    streamingRequest.Fail(detail);
                    OrcaRuntimeDiagnostics.Record("Chat stream failure", streamingRequest.DiagnosticStatus + "\n" + OrcaRuntimeDiagnostics.ExceptionText(ex));
                }
                LlmConnectionTester.ReportFailedCall(ex.GetType().Name + ": " + ex.Message);
            }
        }

    }
}
