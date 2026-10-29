using System.Text.Json.Nodes;

namespace RimAgent.Core;

public static class McpToolOutput
{
    public static JsonObject Create(ToolReply reply)
    {
        var structured = WireJson.Node(reply)!.AsObject();
        var content = new JsonArray();
        if (reply.Success && structured["data"] is JsonObject payload && payload["result"] is JsonObject result
            && result["image"] is JsonObject image && image["data"] is JsonValue data)
        {
            content.Add(new JsonObject { ["type"] = "image", ["mimeType"] = image["mimeType"]!.DeepClone(), ["data"] = data.DeepClone() });
            image.Remove("data"); image["delivery"] = "MCP image content block";
        }
        content.Insert(0, new JsonObject { ["type"] = "text", ["text"] = structured.ToJsonString() });
        return new JsonObject { ["content"] = content, ["structuredContent"] = structured, ["isError"] = !reply.Success };
    }
}
