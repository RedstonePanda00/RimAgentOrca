using System.Text.Json.Nodes;

namespace RimAgent.Core;

public static class ToolCatalog
{
    private static JsonObject Text(string description) => new() { ["type"] = "string", ["description"] = description };
    private static JsonObject Integer(string description, int min, int max) => new() { ["type"] = "integer", ["description"] = description, ["minimum"] = min, ["maximum"] = max };
    private static JsonObject Boolean(string description) => new() { ["type"] = "boolean", ["description"] = description };
    public static JsonArray List()
    {
        var tools = new JsonArray();
        Add("rimagent_session_get", "Read readiness, game session ID, maps, tick and current controller. Works at the main menu.", true, []);
        Add("rimagent_agents_list", "List configured in-mod analysis agents. No provider secrets are returned.", true, ["sessionId"]);
        Add("rimagent_colony_get", "Read structured colony and pawn state for an explicit map; includes stable pawn IDs and draft state.", true, ["sessionId", "mapId"]);
        Add("rimagent_controller_acquire", "Choose narrative OR operation mode for this connection. One controller per mode may coexist; open separate conversations/connections to act as narrator and player. Narrative replaces internal chat/planning; operation controls player actions. Works without any provider, including configuration at the main menu.", false, ["sessionId", "owner", "mode", "requestId"], ("ttlSeconds", Integer("Lease duration; renew before it expires. Default 300.", 30, 900)));
        Add("rimagent_controller_renew", "Renew the current controller lease and read historyEpoch and retained history counts.", false, ["sessionId", "leaseToken"], ("ttlSeconds", Integer("Lease duration, default 300.", 30, 900)));
        Add("rimagent_controller_checkpoint", "Acknowledge and discard this controller's completed task/worker results and action receipts to continue indefinitely without reloading or releasing control. Read/save needed results first; finish/cancel all active tasks and complete/revoke workers. Returns the next historyEpoch, REQUIRED for all subsequent root mutations. Never resubmit an old action in a new epoch. The latest checkpoint can be retried with identical arguments. Does not discard pending narrator messages or another controller's history.", false, ["sessionId", "leaseToken", "requestId", "historyEpoch"]);
        Add("rimagent_controller_release", "Release control and cancel running delegated tasks. Internal RimAgent behavior resumes.", false, ["sessionId", "leaseToken"]);
        Add("rimagent_task_submit", "Delegate a read-only colony analysis to a configured in-mod agent. Returns taskId immediately. Model uses a captured snapshot, cannot issue actions. Poll task_get; cancel when unnecessary.", false,
            ["sessionId", "leaseToken", "requestId", "agentId", "mapId", "instruction"], ("maxTokens", Integer("Output token budget, default 1200.", 128, 4096)));
        Add("rimagent_task_get", "Read delegated task status and model result/usage. Completion is consumed on the main thread even while paused.", true, ["sessionId", "leaseToken", "taskId"]);
        Add("rimagent_task_cancel", "Cancel a delegated task and its pending/network request.", false, ["sessionId", "leaseToken", "taskId"]);
        Add("rimagent_events_read", "Read up to 100 events after a cursor. Advance using the last returned event cursor. gap signals expired history.", true, ["sessionId"], ("afterCursor", Integer("Last consumed event cursor, default 0.", 0, int.MaxValue)));
        Add("rimagent_set_paused", "Set pause state on the main thread, returning observed state. Reuse the same requestId and arguments when retrying an ambiguous result.", false, ["sessionId", "leaseToken", "requestId", "paused"]);
        Add("rimagent_set_drafted", "Set a player's eligible pawn draft state and return observed state. Requires explicit map and pawn ID. Does not move or attack.", false, ["sessionId", "leaseToken", "requestId", "mapId", "pawnId", "drafted"]);
        foreach (var tool in AutonomyCatalog.List()) tools.Add(tool);
        return tools;

        void Add(string name, string description, bool readOnly, string[] required, params (string Key, JsonObject Schema)[] optional)
        {
            var properties = new JsonObject();
            foreach (var key in required)
                properties[key] = key switch
                {
                    "mapId" => Integer("Stable map uniqueID from session_get.", 0, int.MaxValue),
                    "historyEpoch" => Integer("Current history epoch from controller_renew; initially 0.", 0, int.MaxValue),
                    "paused" or "drafted" => Boolean("Desired state."),
                    "sessionId" => Text("Current sessionId from session_get; changes on new/load game."),
                    "leaseToken" => Text("Secret token returned by controller_acquire."),
                    "requestId" => Text("Caller-generated unique mutation ID retained until this controller acknowledges history. Retry with identical arguments and epoch."),
                    _ => Text(key)
                };
            foreach (var (key, schema) in optional) properties[key] = schema;
            properties["grantToken"] = Text("Delegated child capability token. Never share the parent lease token.");
            properties["historyEpoch"] = Integer("Root mutations use the epoch returned by controller_checkpoint; omitted means 0. Never move an old mutation to a new epoch.", 0, int.MaxValue);
            var schemaRequired = required.Where(k => k != "leaseToken");
            tools.Add(new JsonObject {
                ["name"] = name, ["description"] = description,
                ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray(schemaRequired.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()), ["additionalProperties"] = false },
                ["annotations"] = new JsonObject { ["readOnlyHint"] = readOnly, ["destructiveHint"] = false, ["openWorldHint"] = name == "rimagent_task_submit" }
            });
        }
    }
}
