using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using RimAgent.Core;

string? Option(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
var pipeName = Option("--pipe") ?? BridgeWire.DefaultPipeName;
var gameExe = Option("--game-exe");
var saveDataFolder = Option("--save-data-folder");
var allowTestGame = args.Contains("--allow-test-game");
var newInstance = args.Contains("--new-instance");
if (newInstance && (saveDataFolder == null || !allowTestGame)) throw new ArgumentException("--new-instance requires an isolated --save-data-folder and --allow-test-game.");
var launched = new List<Process>();
Console.InputEncoding = System.Text.Encoding.UTF8;
Console.OutputEncoding = new System.Text.UTF8Encoding(false);
var catalog = ToolCatalog.List();
if (gameExe != null)
    catalog.Add(new JsonObject { ["name"] = "rimagent_game_start", ["description"] = "Start the configured local RimWorld executable with the bridge enabled. Does not load or replace a save. Read session_get until ready.",
        ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false } });
if (allowTestGame)
{
    catalog.Add(new JsonObject { ["name"] = "rimagent_test_game_start", ["description"] = "Create a fresh test colony only at the main menu. Requires explicit --allow-test-game launch option. Never replaces a loaded game.",
        ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["storyteller"] = new JsonObject { ["type"] = "string" } }, ["additionalProperties"] = false } });
    catalog.Add(new JsonObject { ["name"] = "rimagent_test_relay", ["description"] = "Isolated test-game harness only: simulate player chat input, request a story cycle, or inspect reply delivery. Requires test launch environment and a narrative controller.",
        ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("chat", "story", "inspect", "gemini", "orca", "chat_probe", "chat_clear", "proactive") }, ["text"] = new JsonObject { ["type"] = "string" } }, ["required"] = new JsonArray("kind"), ["additionalProperties"] = false } });
}
bool initialized = false;
var clientId = Guid.NewGuid().ToString("N");
string? boundMode = Option("--mode");
while (await Console.In.ReadLineAsync() is { } line)
{
    JsonNode? id = null;
    try
    {
        if (line.Length > BridgeWire.MaximumMessageBytes) throw new JsonException("Request too large.");
        var request = JsonNode.Parse(line) as JsonObject ?? throw new JsonException("Expected object.");
        id = request["id"]?.DeepClone();
        if (request["jsonrpc"]?.GetValue<string>() != "2.0") { await Error(id, -32600, "Expected JSON-RPC 2.0."); continue; }
        var method = request["method"]?.GetValue<string>();
        if (id == null) continue;
        object result;
        if (method == "initialize")
        {
            initialized = true;
            var requested = request["params"]?["protocolVersion"]?.GetValue<string>();
            result = new { protocolVersion = requested is "2025-11-25" or "2025-06-18" or "2025-03-26" ? requested : "2025-11-25",
                capabilities = new { tools = new { listChanged = false } }, serverInfo = new { name = "RimAgent", version = "0.1.0" },
                instructions = "Read session_get, then acquire mode narrative OR operation. One conversation/host connection cannot combine both; use separate conversations. No mod LLM provider is needed for external control. Narrative controllers serve game chat/story requests via requests_wait and request_complete. Operation controllers use player tools. Spawn your own child agents and give each a worker_create grantToken and separate MCP connection; never share your root lease token. Children return results via worker_complete. Renew your lease; release when finished. config_get/update expose non-secret settings even at the main menu. API connections/keys remain player-managed. Retry ambiguous mutations with identical requestId and arguments." };
        }
        else if (method == "ping") result = new { };
        else if (!initialized) { await Error(id, -32002, "Initialize first."); continue; }
        else if (method == "tools/list") result = new { tools = catalog };
        else if (method == "tools/call")
        {
            var parameters = request["params"] as JsonObject;
            var name = parameters?["name"]?.GetValue<string>();
            if (name == null || !catalog.Any(t => t?["name"]?.GetValue<string>() == name)) { await Error(id, -32602, "Unknown tool."); continue; }
            if (parameters!["arguments"] != null && parameters["arguments"] is not JsonObject) { await Error(id, -32602, "arguments must be an object."); continue; }
            var arguments = parameters["arguments"] as JsonObject ?? new JsonObject();
            arguments["_clientId"] = clientId;
            if (name == "rimagent_controller_acquire")
            {
                var requestedMode = arguments["mode"]?.GetValue<string>();
                if (boundMode != null && requestedMode != boundMode) { result = McpToolOutput.Create(ToolReply.Fail("mode_conflict", "This host connection is bound to " + boundMode + "; use a separate conversation/connection."));
                    await Console.Out.WriteLineAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = WireJson.Node(result) }.ToJsonString()); continue; }
                if (requestedMode is "narrative" or "operation") boundMode = requestedMode;
            }
            ToolReply reply;
            try
            {
                if (name == "rimagent_game_start")
                {
                    var fullPath = Path.GetFullPath(gameExe!);
                    if (!File.Exists(fullPath)) throw new IOException("Configured game executable does not exist.");
                    var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(fullPath));
                    bool running = processes.Length > 0;
                    foreach (var process in processes) process.Dispose();
                    if (!running || newInstance)
                    {
                        var start = new ProcessStartInfo(fullPath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(fullPath)!, RedirectStandardOutput = true, RedirectStandardError = true };
                        start.Environment["RIMAGENT_BRIDGE"] = "1"; start.Environment["RIMAGENT_PIPE"] = pipeName;
                        if (saveDataFolder != null) start.ArgumentList.Add("-savedatafolder=" + Path.GetFullPath(saveDataFolder));
                        if (allowTestGame) start.Environment["RIMAGENT_ALLOW_TEST_GAME"] = "1";
                        var process = new Process { StartInfo = start };
                        process.OutputDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };
                        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };
                        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); launched.Add(process);
                    }
                    reply = ToolReply.Ok(new { started = !running || newInstance, alreadyRunning = running && !newInstance, readiness = "Poll session_get; startup can take several minutes." });
                }
                else
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    if (name == "rimagent_requests_wait")
                    {
                        int seconds = arguments.Number("timeoutSeconds", 0, 15, 10);
                        var until = DateTime.UtcNow.AddSeconds(seconds);
                        do
                        {
                            reply = await BridgeWire.CallAsync(pipeName, name, arguments, timeout.Token);
                            if (!reply.Success || reply.Data?["items"] is not JsonArray items || items.Count > 0 || DateTime.UtcNow >= until) break;
                            await Task.Delay(500, timeout.Token);
                        } while (true);
                    }
                    else reply = await BridgeWire.CallAsync(pipeName, name, arguments, timeout.Token);
                }
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
            { reply = ToolReply.Fail("bridge_unavailable", ex.Message + " Ensure RimWorld 1.7 and the mod bridge are running. A timed-out mutation may have executed; retry its requestId to resolve."); }
            result = McpToolOutput.Create(reply);
        }
        else { await Error(id, -32601, "Method not found."); continue; }
        await Console.Out.WriteLineAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = WireJson.Node(result) }.ToJsonString());
    }
    catch (JsonException ex) { await Error(id, -32700, ex.Message); }
    catch (Exception ex) { await Error(id, -32603, ex.Message); }
}
foreach (var process in launched) process.Dispose();
static Task Error(JsonNode? id, int code, string message) => Console.Out.WriteLineAsync(new JsonObject {
    ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
}.ToJsonString());
