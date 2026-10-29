using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RimAgent.Core;

public static class McpResponseReader
{
    public static async Task<string> ReadAsync(Stream stream, bool sse, string expectedId, CancellationToken cancellation)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var text = new StringBuilder();
        if (!sse)
        {
            char[] buffer = new char[4096];
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) > 0)
            {
                text.Append(buffer, 0, count); CheckSize(text.Length);
            }
            if (!Matches(text.ToString(), expectedId)) throw new JsonException("MCP response ID does not match request.");
            return text.ToString();
        }
        int total = 0;
        var line = new StringBuilder();
        char[] linesBuffer = new char[4096];
        bool afterCarriageReturn = false;
        int read;
        while ((read = await reader.ReadAsync(linesBuffer.AsMemory(), cancellation).ConfigureAwait(false)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                CheckSize(++total);
                char c = linesBuffer[i];
                if (afterCarriageReturn && c == '\n') { afterCarriageReturn = false; continue; }
                afterCarriageReturn = c == '\r';
                if (c is '\r' or '\n')
                {
                    string? payload = ConsumeLine(line.ToString()); line.Clear();
                    if (payload != null) return payload;
                }
                else line.Append(c);
            }
        }
        if (line.Length > 0) ConsumeLine(line.ToString());
        if (text.Length > 0 && Matches(text.ToString(), expectedId)) return text.ToString();
        throw new JsonException("MCP stream ended without the requested response.");

        string? ConsumeLine(string value)
        {
            if (value.Length == 0)
            {
                string payload = text.ToString(); text.Clear();
                if (payload.Length > 0 && Matches(payload, expectedId)) return payload;
            }
            else if (value.StartsWith("data:", StringComparison.Ordinal))
            {
                if (text.Length > 0) text.Append('\n');
                text.Append(value.AsSpan(5).TrimStart());
            }
            return null;
        }
    }
    private static bool Matches(string payload, string expectedId)
    {
        if (payload == "[DONE]") return false;
        var message = JsonNode.Parse(payload) as JsonObject;
        return message?["id"]?.ToJsonString() == expectedId;
    }
    private static void CheckSize(int length)
    {
        if (length > 1024 * 1024) throw new IOException("MCP response exceeds 1,048,576 decoded characters.");
    }
}
