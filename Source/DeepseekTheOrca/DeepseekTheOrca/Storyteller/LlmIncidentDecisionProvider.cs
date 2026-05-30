using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class LlmIncidentDecisionProvider : IAiDecisionProvider
    {
        private readonly LlmApiClient client = new LlmApiClient();
        private readonly List<LlmChatMessage> messages = new List<LlmChatMessage>();

        private Task<LlmChatResponse> pendingRequest;
        private int targetSeed = int.MinValue;
        private int requestCount;
        private int toolCallCount;
        private string lastStatus = "";
        private readonly List<string> logLines = new List<string>();

        public bool HasPendingWork
        {
            get { return pendingRequest != null || messages.Count > 0; }
        }

        public string LastStatus
        {
            get { return lastStatus; }
        }

        public IEnumerable<string> LogLines
        {
            get { return logLines; }
        }

        public void ClearLog()
        {
            logLines.Clear();
        }

        public AiIncidentPlan SelectIncidentPlan(AiToolContext context)
        {
            if (context == null || context.target == null)
            {
                SetStatus("missing target");
                Reset();
                return null;
            }

            if (targetSeed != int.MinValue && targetSeed != context.target.ConstantRandSeed)
            {
                Reset();
            }

            if (pendingRequest == null && messages.Count == 0)
            {
                StartLoop(context);
                SetStatus("started LLM incident tool-call loop");
                return null;
            }

            if (pendingRequest == null)
            {
                StartRequest();
                SetStatus("sent LLM incident request");
                return null;
            }

            if (!pendingRequest.IsCompleted)
            {
                SetStatus("waiting for LLM incident response");
                return null;
            }

            LlmChatResponse response;
            try
            {
                response = pendingRequest.Result;
            }
            catch (Exception ex)
            {
                LogDebug("LLM incident request failed: " + ex.GetType().Name + ": " + ex.Message);
                SetStatus("request failed: " + ex.Message);
                Reset();
                return null;
            }

            pendingRequest = null;
            if (!response.success)
            {
                LogDebug("LLM incident request failed: " + response.errorMessage);
                SetStatus("request failed: " + response.errorMessage);
                Reset();
                return null;
            }

            AiIncidentPlan plan = HandleResponse(context, response);
            if (plan != null)
            {
                SetStatus("selected " + plan.incidentDefName);
                Reset();
                return plan;
            }

            if (messages.Count == 0)
            {
                return null;
            }

            if (requestCount >= MaxRequestsPerLoop)
            {
                LogDebug("LLM incident tool-call loop reached request budget.");
                SetStatus("request budget reached");
                Reset();
                return null;
            }

            StartRequest();
            return null;
        }

        private void StartLoop(AiToolContext context)
        {
            targetSeed = context.target.ConstantRandSeed;
            requestCount = 0;
            toolCallCount = 0;
            messages.Clear();
            logLines.Clear();

            AddLog("Starting planner for target seed " + targetSeed + ".");
            messages.Add(LlmChatMessage.System(
                "You are a RimWorld storyteller incident planner. "
                + "You may inspect the game only through tools. "
                + "Never invent IncidentDef names; choose only defNames returned by list_available_incidents. "
                + "Do not finish with prose, Markdown, tables, analysis, or a narrative explanation. "
                + "The only valid successful ending is a tool call to schedule_incident. "
                + "When you have selected one safe event, you must call schedule_incident with incidentDef, pointsFactor, and reason. "
                + "If no incident is safe, return exactly this JSON and nothing else: {\"noIncident\":true,\"reason\":\"...\"}."));

            messages.Add(LlmChatMessage.User(
                "Plan one storyteller incident for the current colony. "
                + "Start by calling get_colony_summary and list_available_incidents. "
                + "Use can_fire_incident when uncertain. "
                + "Do not explain your reasoning in text. "
                + "Do not output Markdown. "
                + "Finish by calling schedule_incident with incidentDef, pointsFactor, and reason."));

            StartRequest();
        }

        private void StartRequest()
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !settings.HasConfiguredLlm)
            {
                Reset();
                return;
            }

            pendingRequest = client.SendChatCompletionAsync(settings, new List<LlmChatMessage>(messages), OrcaLlmModelRole.Decision);
            requestCount++;
            SetStatus("request " + requestCount + " sent");
            AddLog("Sent request " + requestCount + " to decision model " + settings.ModelForRole(OrcaLlmModelRole.Decision) + ".");
        }

        private AiIncidentPlan HandleResponse(AiToolContext context, LlmChatResponse response)
        {
            if (response.toolCalls.Count > 0)
            {
                messages.Add(LlmChatMessage.Assistant(response.content, response.toolCalls));
                SetStatus("received " + response.toolCalls.Count + " tool call(s)");
                AddLog("Received " + response.toolCalls.Count + " tool call(s).");

                AiToolSession session = new AiToolSession(context);
                foreach (LlmToolCall toolCall in response.toolCalls)
                {
                    Dictionary<string, string> arguments = ParseArguments(toolCall.argumentsJson);
                    AddLog("Tool call: " + toolCall.name + " " + FormatArguments(arguments));
                    AiToolResult result = InvokeTool(session, toolCall.name, arguments);
                    AddLog("Tool result: " + (result.success ? "ok" : "failed") + " - " + result.message + FormatValues(result));
                    messages.Add(LlmChatMessage.Tool(toolCall.id, SerializeToolResult(result)));

                    if (toolCall.name == "schedule_incident" && result.success)
                    {
                        SetStatus("schedule_incident validated");
                        return PlanFromArguments(arguments);
                    }
                }

                return null;
            }

            if (!string.IsNullOrEmpty(response.content))
            {
                AiIncidentPlan plan = TryParseFinalPlan(response.content);
                if (plan != null)
                {
                    return plan;
                }

                LogDebug("LLM incident planner returned final text without a valid incident plan: " + response.content);
                SetStatus("final text without valid plan");
                AddLog("Final text without valid plan: " + response.content);
            }

            Reset();
            return null;
        }

        private AiToolResult InvokeTool(AiToolSession session, string toolName, Dictionary<string, string> arguments)
        {
            toolCallCount++;
            if (toolCallCount > MaxToolCalls)
            {
                return AiToolResult.Fail("tool call budget exceeded");
            }

            return session.Invoke(toolName, arguments);
        }

        private static Dictionary<string, string> ParseArguments(string argumentsJson)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(argumentsJson))
            {
                return result;
            }

            result["__rawJson"] = argumentsJson;
            try
            {
                Dictionary<string, object> parsed = MiniJson.Deserialize(argumentsJson) as Dictionary<string, object>;
                if (parsed == null)
                {
                    return result;
                }

                foreach (KeyValuePair<string, object> pair in parsed)
                {
                    result[pair.Key] = pair.Value == null ? "" : pair.Value.ToString();
                }
            }
            catch (Exception ex)
            {
                result["parseError"] = ex.Message;
            }

            return result;
        }

        private static string SerializeToolResult(AiToolResult result)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["success"] = result.success;
            payload["message"] = result.message ?? "";
            payload["values"] = result.values;
            return MiniJson.Serialize(payload);
        }

        private static AiIncidentPlan PlanFromArguments(Dictionary<string, string> arguments)
        {
            string incidentDef;
            if (!arguments.TryGetValue("incidentDef", out incidentDef) || string.IsNullOrEmpty(incidentDef))
            {
                return null;
            }

            float pointsFactor = 1f;
            string pointsFactorText;
            if (arguments.TryGetValue("pointsFactor", out pointsFactorText))
            {
                float.TryParse(pointsFactorText, out pointsFactor);
            }

            string reason;
            arguments.TryGetValue("reason", out reason);

            return AiIncidentPlan.For(incidentDef, reason ?? "LLM selected this incident.", pointsFactor);
        }

        private static AiIncidentPlan TryParseFinalPlan(string content)
        {
            try
            {
                Dictionary<string, object> parsed = MiniJson.Deserialize(ExtractJsonObject(content)) as Dictionary<string, object>;
                if (parsed == null)
                {
                    return null;
                }

                object noIncident;
                if (parsed.TryGetValue("noIncident", out noIncident) && noIncident is bool && (bool)noIncident)
                {
                    return null;
                }

                string incidentDef = GetString(parsed, "incidentDef");
                if (string.IsNullOrEmpty(incidentDef))
                {
                    incidentDef = GetString(parsed, "incidentDefName");
                }

                if (string.IsNullOrEmpty(incidentDef))
                {
                    return null;
                }

                float pointsFactor = 1f;
                string pointsFactorText = GetString(parsed, "pointsFactor");
                if (!string.IsNullOrEmpty(pointsFactorText))
                {
                    float.TryParse(pointsFactorText, out pointsFactor);
                }

                return AiIncidentPlan.For(incidentDef, GetString(parsed, "reason") ?? "LLM selected this incident.", pointsFactor);
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractJsonObject(string content)
        {
            int start = content.IndexOf('{');
            int end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return content.Substring(start, end - start + 1);
            }

            return content;
        }

        private static string GetString(Dictionary<string, object> parsed, string key)
        {
            object value;
            if (!parsed.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            return value.ToString();
        }

        private void Reset()
        {
            pendingRequest = null;
            messages.Clear();
            targetSeed = int.MinValue;
            requestCount = 0;
            toolCallCount = 0;
        }

        private void SetStatus(string status)
        {
            lastStatus = status;
        }

        private void AddLog(string line)
        {
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            logLines.Add("tick " + tick + " | " + line);
            while (logLines.Count > 200)
            {
                logLines.RemoveAt(0);
            }
        }

        private static string FormatArguments(Dictionary<string, string> arguments)
        {
            if (arguments == null || arguments.Count == 0)
            {
                return "{}";
            }

            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string> pair in arguments)
            {
                if (pair.Key == "__rawJson")
                {
                    continue;
                }

                parts.Add(pair.Key + "=" + pair.Value);
            }

            return "{" + string.Join(", ", parts.ToArray()) + "}";
        }

        private static string FormatValues(AiToolResult result)
        {
            if (result.values == null || result.values.Count == 0)
            {
                return "";
            }

            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string> pair in result.values)
            {
                parts.Add(pair.Key + "=" + pair.Value);
            }

            return " [" + string.Join(", ", parts.ToArray()) + "]";
        }

        private static int MaxRequestsPerLoop
        {
            get { return 6; }
        }

        private static int MaxToolCalls
        {
            get
            {
                DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
                return settings == null ? 8 : settings.maxToolCalls;
            }
        }

        private static void LogDebug(string message)
        {
            if (DeepseekTheOrcaMod.Settings != null && DeepseekTheOrcaMod.Settings.debugLogging)
            {
                Log.Message("[RimAgent] " + message);
            }
        }
    }
}
