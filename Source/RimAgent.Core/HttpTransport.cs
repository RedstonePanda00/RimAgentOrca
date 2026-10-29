using System.Collections.Concurrent;
using System.Net;

namespace RimAgent.Core;

public static class HttpTransport
{
    private static readonly ConcurrentDictionary<string, Lazy<SocketsHttpHandler>> handlers = new(StringComparer.Ordinal);
    public static HttpClient CreateClient(string? proxyUrl = null)
    {
        string key = proxyUrl?.Trim() ?? "";
        var handler = handlers.GetOrAdd(key, proxy => new Lazy<SocketsHttpHandler>(() => new SocketsHttpHandler {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5), PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            UseCookies = false, Proxy = proxy.Length == 0 ? null : new WebProxy(proxy), UseProxy = true
        })).Value;
        // A lightweight client has independent headers/timeouts; the transport pool is shared.
        return new HttpClient(handler, disposeHandler: false);
    }
}
