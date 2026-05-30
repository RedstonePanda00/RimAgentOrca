using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaMcpToolDescriptor
    {
        public string exposedName;
        public string serverName;
        public string serverToolName;
        public string url;
        public string bearerToken;
        public int maxResultChars;
        public string description;
        public Dictionary<string, object> inputSchema;
    }

    public static class OrcaHttpMcpClient
    {
        private const string RawArgumentsKey = "__rawJson";
        private const string ProtocolVersion = "2025-03-26";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan CacheFreshDuration = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan FailedRefreshBackoff = TimeSpan.FromSeconds(10);
        private static readonly object syncRoot = new object();
        private static readonly List<OrcaMcpToolDescriptor> cachedTools = new List<OrcaMcpToolDescriptor>();
        private static readonly Dictionary<string, string> sessionIdsByEndpoint = new Dictionary<string, string>();
        private static readonly HashSet<string> initializedEndpoints = new HashSet<string>();
        private static Task<List<OrcaMcpToolDescriptor>> pendingDiscovery;
        private static string pendingFingerprint = "";
        private static string cachedFingerprint = "";
        private static DateTime cacheTimeUtc = DateTime.MinValue;
        private static DateTime lastRefreshAttemptUtc = DateTime.MinValue;
        private static string lastRefreshAttemptFingerprint = "";
        private static string lastDiscoveryError = "";
        private static int nextRequestId = 1;

        public static List<OrcaMcpToolDescriptor> DiscoverTools()
        {
            Tick();
            return CachedTools();
        }

        public static void Tick()
        {
            List<OrcaHttpMcpServerSettings> servers = ActiveServers();
            if (servers.Count == 0)
            {
                ClearCacheIfNeeded();
                return;
            }

            string fingerprint = Fingerprint(servers);
            CompletePendingDiscovery(fingerprint);
            StartDiscoveryIfNeeded(servers, fingerprint);
        }

        public static string LastDiscoveryError
        {
            get
            {
                lock (syncRoot)
                {
                    return lastDiscoveryError;
                }
            }
        }

        public static bool DiscoveryInProgress
        {
            get
            {
                lock (syncRoot)
                {
                    return pendingDiscovery != null;
                }
            }
        }

        private static List<OrcaMcpToolDescriptor> CachedTools()
        {
            lock (syncRoot)
            {
                return CloneTools(cachedTools);
            }
        }

        private static void ClearCacheIfNeeded()
        {
            lock (syncRoot)
            {
                if (cachedTools.Count == 0 && cachedFingerprint.NullOrEmpty() && pendingDiscovery == null)
                {
                    return;
                }

                pendingDiscovery = null;
                pendingFingerprint = "";
                cachedFingerprint = "";
                cacheTimeUtc = DateTime.MinValue;
                lastRefreshAttemptFingerprint = "";
                lastDiscoveryError = "";
                cachedTools.Clear();
            }
        }

        private static void CompletePendingDiscovery(string currentFingerprint)
        {
            Task<List<OrcaMcpToolDescriptor>> task = null;
            string taskFingerprint = "";
            lock (syncRoot)
            {
                if (pendingDiscovery == null || !pendingDiscovery.IsCompleted)
                {
                    return;
                }

                task = pendingDiscovery;
                taskFingerprint = pendingFingerprint;
                pendingDiscovery = null;
                pendingFingerprint = "";
            }

            try
            {
                List<OrcaMcpToolDescriptor> discovered = task.Result ?? new List<OrcaMcpToolDescriptor>();
                if (taskFingerprint != currentFingerprint)
                {
                    DebugLog("MCP tool discovery result ignored because server settings changed.");
                    return;
                }

                lock (syncRoot)
                {
                    cachedFingerprint = taskFingerprint;
                    cacheTimeUtc = DateTime.UtcNow;
                    lastDiscoveryError = "";
                    cachedTools.Clear();
                    cachedTools.AddRange(discovered);
                }

                DebugLog("MCP tool discovery completed; cached " + discovered.Count + " tool(s).");
            }
            catch (Exception ex)
            {
                string message = ex.GetType().Name + ": " + ex.Message;
                lock (syncRoot)
                {
                    lastDiscoveryError = message;
                }

                DebugLog("MCP tool discovery failed: " + message);
            }
        }

        private static void StartDiscoveryIfNeeded(List<OrcaHttpMcpServerSettings> servers, string fingerprint)
        {
            DateTime now = DateTime.UtcNow;
            lock (syncRoot)
            {
                if (pendingDiscovery != null)
                {
                    return;
                }

                if (cachedFingerprint == fingerprint && now - cacheTimeUtc < CacheFreshDuration)
                {
                    return;
                }

                if (lastRefreshAttemptFingerprint == fingerprint && now - lastRefreshAttemptUtc < FailedRefreshBackoff)
                {
                    return;
                }

                if (cachedFingerprint != fingerprint)
                {
                    cachedFingerprint = "";
                    cacheTimeUtc = DateTime.MinValue;
                    lastDiscoveryError = "";
                    cachedTools.Clear();
                }

                lastRefreshAttemptUtc = now;
                lastRefreshAttemptFingerprint = fingerprint;
                pendingFingerprint = fingerprint;
                List<OrcaHttpMcpServerSettings> snapshot = CloneServers(servers);
                pendingDiscovery = Task.Run(delegate
                {
                    return DiscoverToolsForServers(snapshot);
                });
            }

            DebugLog("MCP tool discovery started in background.");
        }

        private static List<OrcaMcpToolDescriptor> DiscoverToolsForServers(List<OrcaHttpMcpServerSettings> servers)
        {
            List<OrcaMcpToolDescriptor> discovered = new List<OrcaMcpToolDescriptor>();
            HashSet<string> usedNames = new HashSet<string>();
            bool includeServerName = servers.Count > 1;
            for (int i = 0; i < servers.Count; i++)
            {
                OrcaHttpMcpServerSettings server = servers[i];
                try
                {
                    discovered.AddRange(DiscoverToolsNow(server, includeServerName, usedNames));
                }
                catch (Exception ex)
                {
                    DebugLog("MCP tool discovery failed for " + ServerLabel(server) + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            return discovered;
        }

        public static bool IsExposedTool(string toolName)
        {
            return DiscoverTools().Any(tool => tool.exposedName == toolName);
        }

        public static bool TryInvokeExposedTool(string toolName, Dictionary<string, string> arguments, out AiToolResult result)
        {
            OrcaMcpToolDescriptor descriptor = DiscoverTools().FirstOrDefault(tool => tool.exposedName == toolName);
            if (descriptor == null)
            {
                result = null;
                return false;
            }

            try
            {
                result = InvokeToolNow(descriptor, arguments ?? new Dictionary<string, string>());
            }
            catch (Exception ex)
            {
                result = AiToolResult.Fail("MCP tool failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            return true;
        }

        private static List<OrcaMcpToolDescriptor> DiscoverToolsNow(OrcaHttpMcpServerSettings server, bool includeServerName, HashSet<string> usedNames)
        {
            EnsureInitialized(server);
            Dictionary<string, object> response = SendRequest(server, "tools/list", new Dictionary<string, object>()).GetAwaiter().GetResult();
            Dictionary<string, object> result = GetDictionary(response, "result");
            object toolsObj;
            List<object> tools = result != null && result.TryGetValue("tools", out toolsObj) ? toolsObj as List<object> : null;
            if (tools == null)
            {
                return new List<OrcaMcpToolDescriptor>();
            }

            List<OrcaMcpToolDescriptor> descriptors = new List<OrcaMcpToolDescriptor>();
            foreach (object toolObj in tools)
            {
                Dictionary<string, object> tool = toolObj as Dictionary<string, object>;
                if (tool == null)
                {
                    continue;
                }

                string toolName = GetString(tool, "name");
                if (toolName.NullOrEmpty())
                {
                    continue;
                }

                string exposedBase = includeServerName
                    ? "mcp_" + SanitizeName(ServerLabel(server)) + "_" + SanitizeName(toolName)
                    : "mcp_" + SanitizeName(toolName);
                string exposedName = UniqueToolName(exposedBase, usedNames);
                usedNames.Add(exposedName);
                Dictionary<string, object> inputSchema = GetDictionary(tool, "inputSchema") ?? EmptyParameters();
                descriptors.Add(new OrcaMcpToolDescriptor
                {
                    exposedName = exposedName,
                    serverName = ServerLabel(server),
                    serverToolName = toolName,
                    url = server.url,
                    bearerToken = server.bearerToken ?? "",
                    maxResultChars = MaxResultChars(),
                    description = "External HTTP MCP tool from " + ServerLabel(server) + ": " + (GetString(tool, "description") ?? toolName),
                    inputSchema = inputSchema
                });
            }

            return descriptors;
        }

        private static AiToolResult InvokeToolNow(OrcaMcpToolDescriptor descriptor, Dictionary<string, string> arguments)
        {
            if (descriptor.url.NullOrEmpty())
            {
                return AiToolResult.Fail("HTTP MCP endpoint is not configured");
            }

            Dictionary<string, object> parameters = new Dictionary<string, object>();
            parameters["name"] = descriptor.serverToolName;
            parameters["arguments"] = ParseRawArguments(arguments);

            OrcaHttpMcpServerSettings server = new OrcaHttpMcpServerSettings
            {
                name = descriptor.serverName,
                enabled = true,
                url = descriptor.url,
                bearerToken = descriptor.bearerToken
            };

            Dictionary<string, object> response = SendRequest(server, "tools/call", parameters).GetAwaiter().GetResult();
            Dictionary<string, object> rpcError = GetDictionary(response, "error");
            if (rpcError != null)
            {
                return AiToolResult.Fail("MCP JSON-RPC error: " + (GetString(rpcError, "message") ?? MiniJson.Serialize(rpcError)));
            }

            Dictionary<string, object> result = GetDictionary(response, "result");
            if (result == null)
            {
                return AiToolResult.Fail("MCP response did not contain result");
            }

            string content = ExtractToolContent(result);
            bool isError = GetBool(result, "isError");
            content = Limit(content, descriptor.maxResultChars);
            AiToolResult toolResult = isError
                ? AiToolResult.Fail(content.NullOrEmpty() ? "MCP tool returned an error" : content)
                : AiToolResult.Ok(content.NullOrEmpty() ? "MCP tool returned no content" : "MCP tool returned content");
            toolResult.WithValue("mcpServer", descriptor.serverName);
            toolResult.WithValue("mcpTool", descriptor.serverToolName);
            if (!content.NullOrEmpty())
            {
                toolResult.WithValue("content", content);
            }

            return toolResult;
        }

        private static void EnsureInitialized(OrcaHttpMcpServerSettings server)
        {
            string endpointKey = EndpointKey(server);
            lock (syncRoot)
            {
                if (initializedEndpoints.Contains(endpointKey))
                {
                    return;
                }

                sessionIdsByEndpoint.Remove(endpointKey);
            }

            Dictionary<string, object> parameters = new Dictionary<string, object>();
            parameters["protocolVersion"] = ProtocolVersion;
            parameters["capabilities"] = new Dictionary<string, object>();
            parameters["clientInfo"] = new Dictionary<string, object>
            {
                { "name", "RimAgent" },
                { "version", "0.1" }
            };

            SendRequest(server, "initialize", parameters).GetAwaiter().GetResult();
            SendNotification(server, "notifications/initialized").GetAwaiter().GetResult();
            lock (syncRoot)
            {
                initializedEndpoints.Add(endpointKey);
            }
        }

        private static async Task<Dictionary<string, object>> SendRequest(OrcaHttpMcpServerSettings server, string method, Dictionary<string, object> parameters)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["jsonrpc"] = "2.0";
            payload["id"] = NextRequestId();
            payload["method"] = method;
            payload["params"] = parameters ?? new Dictionary<string, object>();

            string responseText = await PostJson(server, MiniJson.Serialize(payload)).ConfigureAwait(false);
            Dictionary<string, object> response = MiniJson.Deserialize(responseText) as Dictionary<string, object>;
            if (response == null)
            {
                throw new InvalidOperationException("MCP response was not a JSON object.");
            }

            Dictionary<string, object> error = GetDictionary(response, "error");
            if (error != null)
            {
                throw new InvalidOperationException(GetString(error, "message") ?? MiniJson.Serialize(error));
            }

            return response;
        }

        private static async Task SendNotification(OrcaHttpMcpServerSettings server, string method)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["jsonrpc"] = "2.0";
            payload["method"] = method;
            await PostJson(server, MiniJson.Serialize(payload)).ConfigureAwait(false);
        }

        private static async Task<string> PostJson(OrcaHttpMcpServerSettings server, string json)
        {
            using (HttpClient client = new HttpClient())
            {
                string endpointKey = EndpointKey(server);
                client.Timeout = Timeout;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                client.DefaultRequestHeaders.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
                string sessionId;
                lock (syncRoot)
                {
                    sessionIdsByEndpoint.TryGetValue(endpointKey, out sessionId);
                }
                if (!sessionId.NullOrEmpty())
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
                }
                if (!server.bearerToken.NullOrEmpty())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.bearerToken.Trim());
                }

                using (StringContent content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    HttpResponseMessage response = await client.PostAsync(server.url.Trim(), content).ConfigureAwait(false);
                    IEnumerable<string> sessionHeaders;
                    if (response.Headers.TryGetValues("Mcp-Session-Id", out sessionHeaders))
                    {
                        lock (syncRoot)
                        {
                            sessionIdsByEndpoint[endpointKey] = sessionHeaders.FirstOrDefault() ?? sessionId;
                        }
                    }

                    string responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException("HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + ": " + Limit(responseText, 500));
                    }

                    return ExtractJsonPayload(responseText);
                }
            }
        }

        private static object ParseRawArguments(Dictionary<string, string> arguments)
        {
            string rawJson;
            if (arguments.TryGetValue(RawArgumentsKey, out rawJson) && !rawJson.NullOrEmpty())
            {
                object parsed = MiniJson.Deserialize(rawJson);
                if (parsed is Dictionary<string, object>)
                {
                    return parsed;
                }
            }

            Dictionary<string, object> fallback = new Dictionary<string, object>();
            foreach (KeyValuePair<string, string> pair in arguments)
            {
                if (pair.Key == RawArgumentsKey || pair.Key == "parseError")
                {
                    continue;
                }

                fallback[pair.Key] = pair.Value;
            }

            return fallback;
        }

        private static List<OrcaHttpMcpServerSettings> ActiveServers()
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !settings.enableHttpMcp || settings.httpMcpServers == null)
            {
                return new List<OrcaHttpMcpServerSettings>();
            }

            return settings.httpMcpServers
                .Where(server => server != null && server.enabled && !server.url.NullOrEmpty())
                .ToList();
        }

        private static List<OrcaHttpMcpServerSettings> CloneServers(List<OrcaHttpMcpServerSettings> servers)
        {
            List<OrcaHttpMcpServerSettings> result = new List<OrcaHttpMcpServerSettings>();
            for (int i = 0; i < servers.Count; i++)
            {
                OrcaHttpMcpServerSettings server = servers[i];
                if (server == null)
                {
                    continue;
                }

                result.Add(new OrcaHttpMcpServerSettings
                {
                    name = server.name,
                    enabled = server.enabled,
                    url = server.url,
                    bearerToken = server.bearerToken
                });
            }

            return result;
        }

        private static string Fingerprint(List<OrcaHttpMcpServerSettings> servers)
        {
            StringBuilder builder = new StringBuilder();
            foreach (OrcaHttpMcpServerSettings server in servers)
            {
                builder.Append(ServerLabel(server)).Append('|')
                    .Append(server.url ?? "").Append('|')
                    .Append(server.bearerToken ?? "").Append(';');
            }

            builder.Append(MaxResultChars());
            return builder.ToString();
        }

        private static int MaxResultChars()
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            return settings == null ? 6000 : settings.httpMcpMaxResultChars;
        }

        private static string ServerLabel(OrcaHttpMcpServerSettings server)
        {
            return server == null || server.name.NullOrEmpty() ? "MCP" : server.name.Trim();
        }

        private static string EndpointKey(OrcaHttpMcpServerSettings server)
        {
            return (server.url ?? "").Trim() + "|" + (server.bearerToken ?? "");
        }

        private static string ExtractToolContent(Dictionary<string, object> result)
        {
            List<string> parts = new List<string>();
            object contentObj;
            List<object> content = result.TryGetValue("content", out contentObj) ? contentObj as List<object> : null;
            if (content != null)
            {
                foreach (object itemObj in content)
                {
                    Dictionary<string, object> item = itemObj as Dictionary<string, object>;
                    if (item == null)
                    {
                        continue;
                    }

                    string text = GetString(item, "text");
                    if (!text.NullOrEmpty())
                    {
                        parts.Add(text);
                    }
                    else
                    {
                        parts.Add(MiniJson.Serialize(item));
                    }
                }
            }

            Dictionary<string, object> structuredContent = GetDictionary(result, "structuredContent");
            if (structuredContent != null)
            {
                parts.Add(MiniJson.Serialize(structuredContent));
            }

            if (parts.Count > 0)
            {
                return string.Join("\n", parts.ToArray());
            }

            return MiniJson.Serialize(result);
        }

        private static string ExtractJsonPayload(string responseText)
        {
            if (responseText.NullOrEmpty())
            {
                return "{}";
            }

            string trimmed = responseText.Trim();
            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            {
                return trimmed;
            }

            StringBuilder builder = new StringBuilder();
            string[] lines = responseText.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("data:"))
                {
                    string data = line.Substring(5).Trim();
                    if (data == "[DONE]")
                    {
                        continue;
                    }
                    builder.Append(data);
                }
            }

            string payload = builder.ToString().Trim();
            return payload.NullOrEmpty() ? "{}" : payload;
        }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static string GetString(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null ? value.ToString() : null;
        }

        private static bool GetBool(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return false;
            }

            return value is bool && (bool)value;
        }

        private static Dictionary<string, object> EmptyParameters()
        {
            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>() }
            };
        }

        private static string UniqueToolName(string baseName, HashSet<string> usedNames)
        {
            string name = LimitToolName(baseName);
            if (!usedNames.Contains(name))
            {
                return name;
            }

            for (int i = 2; i < 1000; i++)
            {
                string suffix = "_" + i;
                string candidate = LimitToolName(baseName, suffix.Length) + suffix;
                if (!usedNames.Contains(candidate))
                {
                    return candidate;
                }
            }

            return LimitToolName(baseName + "_tool");
        }

        private static string LimitToolName(string name, int reserve = 0)
        {
            int maxLength = Math.Max(1, 64 - reserve);
            return name.Length <= maxLength ? name : name.Substring(0, maxLength);
        }

        private static string SanitizeName(string name)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
                {
                    builder.Append(c);
                }
                else
                {
                    builder.Append('_');
                }
            }

            return builder.Length == 0 ? "tool" : builder.ToString();
        }

        private static List<OrcaMcpToolDescriptor> CloneTools(List<OrcaMcpToolDescriptor> tools)
        {
            return tools.Select(tool => new OrcaMcpToolDescriptor
            {
                exposedName = tool.exposedName,
                serverName = tool.serverName,
                serverToolName = tool.serverToolName,
                url = tool.url,
                bearerToken = tool.bearerToken,
                maxResultChars = tool.maxResultChars,
                description = tool.description,
                inputSchema = tool.inputSchema
            }).ToList();
        }

        private static int NextRequestId()
        {
            lock (syncRoot)
            {
                return nextRequestId++;
            }
        }

        private static string Limit(string text, int maxChars)
        {
            if (text.NullOrEmpty() || text.Length <= maxChars)
            {
                return text ?? "";
            }

            return text.Substring(0, Math.Max(0, maxChars)) + "\n[truncated]";
        }

        private static void DebugLog(string message)
        {
            if (DeepseekTheOrcaMod.Settings != null && DeepseekTheOrcaMod.Settings.debugLogging)
            {
                Log.Message("[RimAgent] " + message);
            }
        }
    }
}
