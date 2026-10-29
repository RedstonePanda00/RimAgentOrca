using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RimAgent.Core;

namespace RimAgent;

internal static class OrcaWebAccess
{
    public static Task<object> Begin(string tool, JsonObject args, CancellationToken cancellation)
    {
        var settings = RimAgentMod.Settings;
        if (settings?.UsesLocalWebSearchTool != true) throw new BridgeException("search_disabled", "Enable web search in mod settings.");
        var provider = settings.webSearchMode;
        var key = provider == "deepseek" ? settings.deepseekSearchApiKey : settings.tavilyApiKey;
        string proxy = null;
        if (provider == "deepseek" && string.IsNullOrWhiteSpace(key))
        {
            settings.EnsureLlmConnections();
            var connection = settings.llmConnections.FirstOrDefault(c => c.enabled && !string.IsNullOrWhiteSpace(c.apiKey)
                && Uri.TryCreate(c.ActiveBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "api.deepseek.com");
            key = connection?.apiKey;
            proxy = connection?.proxyUrl;
        }
        if (string.IsNullOrWhiteSpace(key)) throw new BridgeException("search_key_missing", "Configure the selected search provider API key.");
        if (tool == "rimagent_web_fetch" && provider != "deepseek") throw new BridgeException("search_provider", "Web fetch requires DeepSeek search mode.");
        var query = args.Text(tool == "rimagent_web_fetch" ? "url" : "query", tool == "rimagent_web_fetch" ? 8192 : 400);
        var count = Math.Min(args.Number("maxResults", 1, 10, settings.tavilyMaxResults), settings.tavilyMaxResults);
        var depth = settings.tavilySearchDepth;
        var topic = args["topic"]?.GetValue<string>() ?? "general";
        var timeRange = args["timeRange"]?.GetValue<string>();
        return Send();
        async Task<object> Send()
        {
            using var client = HttpTransport.CreateClient(proxy);
            var search = new WebSearchClient(client);
            if (tool == "rimagent_web_fetch") return await search.FetchAsync(key, query, cancellation).ConfigureAwait(false);
            return await search.SearchAsync(provider, key, query, count, depth, cancellation, topic, timeRange).ConfigureAwait(false);
        }
    }
}
