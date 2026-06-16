using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimWorld;
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

        public OrcaIncidentCyclePlan SelectIncidentCyclePlan(AiToolContext context, float cycleDays, int cycleBudget)
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
                StartLoop(context, cycleDays, cycleBudget);
                SetStatus("started LLM incident cycle-planning loop");
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

            OrcaIncidentCyclePlan plan = HandleResponse(context, response, cycleDays, cycleBudget);
            if (plan != null)
            {
                SetStatus("planned " + plan.incidents.Count + " scheduled incident(s)");
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

        private void StartLoop(AiToolContext context, float cycleDays, int cycleBudget)
        {
            targetSeed = context.target.ConstantRandSeed;
            requestCount = 0;
            toolCallCount = 0;
            messages.Clear();
            logLines.Clear();

            AddLog("Starting planner for target seed " + targetSeed + ".");
            string narrativeTendency = OrcaChatPersonaManager.CurrentNarrativeTendency();
            messages.Add(LlmChatMessage.System(
                "You are a RimWorld storyteller cycle planner. "
                + "You may inspect the game only through tools. "
                + "Prefer the two core planning tools: get_colony_summary and list_available_incidents; the tool budget is very limited. "
                + "Never invent IncidentDef names; choose only defNames returned by list_available_incidents. "
                + "Plan the full upcoming storyteller cycle, not just one immediate event. "
                + "Do not call schedule_incident; the game script will persist and fire your plan later. "
                + "The cycle budget starts at " + cycleBudget + ". Negative incidents consume budget with negative budgetDelta values, positive incidents restore or add budget with positive budgetDelta values, and neutral incidents use 0. "
                + "You must spend the full cycle budget by the end of the plan: the final remainingBudget must be exactly 0. "
                + "Each event must include offsetDays, incidentDef, pointsFactor, polarity, budgetDelta, remainingBudget, and reason. Keep each reason under 120 characters. "
                + "remainingBudget means the remaining cycle budget after applying that event. "
                + "Do not include prose, Markdown, code fences, tables, or hidden reasoning. "
                + "Return exactly one raw JSON object and no extra text before or after it. "
                + "Schema: {\"cyclePlan\":{\"summary\":\"short planning summary\",\"finalRemainingBudget\":0,\"events\":[{\"offsetDays\":0.5,\"incidentDef\":\"defName from tools\",\"pointsFactor\":1.0,\"polarity\":\"negative_major|negative_minor|positive|neutral\",\"budgetDelta\":-1,\"remainingBudget\":2,\"reason\":\"short story reason\",\"debugBudgetText\":\"optional\"}]}}. "
                + (narrativeTendency.NullOrEmpty() ? "" : "Current persona narrative tendency for planning only: " + narrativeTendency + " ")));

            messages.Add(LlmChatMessage.User(
                "Plan all storyteller incidents for the next "
                + cycleDays.ToString("F2")
                + " in-game days. "
                + "Call get_colony_summary and list_available_incidents once each, then produce the final cyclePlan JSON. "
                + "Do not ask for pawn details or extra validation tools; list_available_incidents already contains incidents that can fire now, and the script will validate again when each event is due. "
                + "Use the local polarity and budget hints from list_available_incidents as guidance, then decide the final budgetDelta and remainingBudget yourself. "
                + "Use at most 3 scheduled events unless the budget cannot be spent otherwise. "
                + "Place each event inside the cycle by offsetDays from 0 to "
                + cycleDays.ToString("F2")
                + ". "
                + "The final event must leave remainingBudget at 0."));

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

            pendingRequest = client.SendChatCompletionWithToolsAsync(
                settings,
                new List<LlmChatMessage>(messages),
                LlmToolSchemas.BuildForRole(OrcaLlmModelRole.Decision),
                1800,
                0.35f,
                OrcaLlmModelRole.Decision);
            requestCount++;
            SetStatus("request " + requestCount + " sent");
            AddLog("Sent request " + requestCount + " to decision model " + settings.ModelForRole(OrcaLlmModelRole.Decision) + ".");
        }

        private OrcaIncidentCyclePlan HandleResponse(AiToolContext context, LlmChatResponse response, float cycleDays, int cycleBudget)
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

                }

                return null;
            }

            if (!string.IsNullOrEmpty(response.content))
            {
                string rejectReason;
                OrcaIncidentCyclePlan plan = TryParseFinalCyclePlan(context, response.content, cycleDays, cycleBudget, out rejectReason);
                if (plan != null)
                {
                    return plan;
                }

                LogDebug("LLM incident planner returned final text without a valid cycle plan: " + rejectReason + " | " + response.content);
                SetStatus("final text without valid cycle plan: " + rejectReason);
                AddLog("Final text without valid cycle plan: " + rejectReason + " | " + response.content);
            }

            Reset();
            return null;
        }

        private AiToolResult InvokeTool(AiToolSession session, string toolName, Dictionary<string, string> arguments)
        {
            toolCallCount++;
            if (toolCallCount > MaxCyclePlanningToolCalls)
            {
                return AiToolResult.Fail("cycle planning tool budget reached; return the final cyclePlan JSON now");
            }

            if (toolCallCount > MaxToolCalls)
            {
                return AiToolResult.Fail("tool call budget exceeded");
            }

            if (toolName == "schedule_incident")
            {
                return AiToolResult.Fail("schedule_incident is disabled for cycle planning; return the final cyclePlan JSON instead");
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

        private static OrcaIncidentCyclePlan TryParseFinalCyclePlan(
            AiToolContext context,
            string content,
            float cycleDays,
            int cycleBudget,
            out string rejectReason)
        {
            rejectReason = "";
            try
            {
                Dictionary<string, object> parsed = MiniJson.Deserialize(ExtractJsonObject(content)) as Dictionary<string, object>;
                if (parsed == null)
                {
                    rejectReason = "response was not a JSON object";
                    return null;
                }

                Dictionary<string, object> cycleObject = parsed;
                object cyclePlanObject;
                if (parsed.TryGetValue("cyclePlan", out cyclePlanObject))
                {
                    cycleObject = cyclePlanObject as Dictionary<string, object>;
                    if (cycleObject == null)
                    {
                        rejectReason = "cyclePlan was not an object";
                        return null;
                    }
                }

                object eventsObject;
                if (!cycleObject.TryGetValue("events", out eventsObject))
                {
                    cycleObject.TryGetValue("incidents", out eventsObject);
                }

                List<object> events = eventsObject as List<object>;
                if (events == null || events.Count == 0)
                {
                    rejectReason = "cycle plan did not contain events";
                    return null;
                }

                int now = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
                int cycleTicks = GenDate.DaysToTicks(cycleDays <= 0f ? 1f : cycleDays);
                OrcaIncidentCyclePlan plan = new OrcaIncidentCyclePlan();
                plan.cycleStartTick = now;
                plan.cycleEndTick = now + cycleTicks;
                plan.cycleBudget = cycleBudget <= 0 ? OrcaIncidentCyclePlan.DefaultCycleBudget : cycleBudget;
                plan.targetSeed = context == null || context.target == null ? 0 : context.target.ConstantRandSeed;
                plan.summary = GetString(cycleObject, "summary") ?? "";

                int lastRemainingBudget = plan.cycleBudget;
                for (int i = 0; i < events.Count; i++)
                {
                    Dictionary<string, object> eventObject = events[i] as Dictionary<string, object>;
                    if (eventObject == null)
                    {
                        rejectReason = "event " + i + " was not an object";
                        return null;
                    }

                    string incidentDef = GetString(eventObject, "incidentDef");
                    if (incidentDef.NullOrEmpty())
                    {
                        incidentDef = GetString(eventObject, "incidentDefName");
                    }
                    if (incidentDef.NullOrEmpty())
                    {
                        rejectReason = "event " + i + " missing incidentDef";
                        return null;
                    }

                    CachedIncidentDef ignored;
                    if (!OrcaIncidentDefCache.TryGet(incidentDef, out ignored))
                    {
                        rejectReason = "event " + i + " used unknown incidentDef: " + incidentDef;
                        return null;
                    }

                    float offsetDays = GetFloat(eventObject, "offsetDays", -1f);
                    if (offsetDays < 0f || offsetDays > cycleDays)
                    {
                        rejectReason = "event " + i + " offsetDays outside cycle";
                        return null;
                    }

                    int budgetDelta;
                    if (!TryGetInt(eventObject, "budgetDelta", out budgetDelta))
                    {
                        rejectReason = "event " + i + " missing budgetDelta";
                        return null;
                    }

                    int remainingBudget;
                    if (!TryGetInt(eventObject, "remainingBudget", out remainingBudget))
                    {
                        rejectReason = "event " + i + " missing remainingBudget";
                        return null;
                    }

                    OrcaScheduledIncidentPlan scheduled = new OrcaScheduledIncidentPlan();
                    scheduled.offsetDays = offsetDays;
                    scheduled.fireTick = now + GenDate.DaysToTicks(offsetDays);
                    scheduled.incidentDefName = incidentDef;
                    scheduled.pointsFactor = GetFloat(eventObject, "pointsFactor", 1f);
                    scheduled.polarity = GetString(eventObject, "polarity") ?? "neutral";
                    scheduled.budgetDelta = budgetDelta;
                    scheduled.remainingBudget = remainingBudget;
                    scheduled.reason = GetString(eventObject, "reason") ?? "Cycle planner selected this incident.";
                    scheduled.debugBudgetText = GetString(eventObject, "debugBudgetText") ?? "";
                    scheduled.targetSeed = plan.targetSeed;
                    plan.incidents.Add(scheduled);
                    lastRemainingBudget = remainingBudget;
                }

                int finalRemainingBudget;
                plan.finalRemainingBudget = TryGetInt(cycleObject, "finalRemainingBudget", out finalRemainingBudget)
                    ? finalRemainingBudget
                    : lastRemainingBudget;
                if (plan.finalRemainingBudget != 0 || lastRemainingBudget != 0)
                {
                    rejectReason = "cycle plan did not spend full budget";
                    return null;
                }

                return plan;
            }
            catch (Exception ex)
            {
                rejectReason = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
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

        private static float GetFloat(Dictionary<string, object> parsed, string key, float defaultValue)
        {
            string text = GetString(parsed, key);
            if (text.NullOrEmpty())
            {
                return defaultValue;
            }

            float value;
            return float.TryParse(text, out value) ? value : defaultValue;
        }

        private static bool TryGetInt(Dictionary<string, object> parsed, string key, out int result)
        {
            result = 0;
            string text = GetString(parsed, key);
            if (text.NullOrEmpty())
            {
                return false;
            }

            float floatValue;
            if (float.TryParse(text, out floatValue))
            {
                result = (int)floatValue;
                return true;
            }

            return int.TryParse(text, out result);
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

        private static int MaxCyclePlanningToolCalls
        {
            get { return 2; }
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
