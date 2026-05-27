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

            return await LlmRequestScheduler.RunAsync("model discovery", async delegate
            {
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
            }).ConfigureAwait(false);
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

            return await LlmRequestScheduler.RunAsync("connection test", async delegate
            {
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
            }).ConfigureAwait(false);
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
    }
}
