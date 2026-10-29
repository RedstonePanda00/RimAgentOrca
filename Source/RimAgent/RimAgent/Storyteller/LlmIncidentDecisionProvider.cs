using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace RimAgent
{
    public sealed class LlmIncidentDecisionProvider : IAiDecisionProvider, IOrcaCancellableDecisionProvider
    {
        private readonly LlmApiClient client = new LlmApiClient();
        private readonly List<LlmChatMessage> messages = new List<LlmChatMessage>();

        private Task<LlmChatResponse> pendingRequest;
        private IEnumerator<bool> pendingTools;
        private int targetSeed = int.MinValue;
        private int requestCount;
        private int toolCallCount;
        private string lastStatus = "";
        private readonly List<string> logLines = new List<string>();
        private readonly IncidentPlanningPolicy suppliedPolicy;
        private IncidentPlanningPolicy policy;
        private DateTime retryAfterUtc;
        private int failedLoops;

        public LlmIncidentDecisionProvider() { }

        public LlmIncidentDecisionProvider(IncidentPlanningPolicy policy) { suppliedPolicy = policy; this.policy = policy; }

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
                // A second map's storyteller interval must not discard the first map's request.
                if (Find.Maps.Any(map => map.ConstantRandSeed == targetSeed)) return null;
                Reset();
            }

            if (pendingTools != null)
            {
                if (pendingTools.MoveNext()) { SetStatus("waiting for tool result"); return null; }
                pendingTools.Dispose();
                pendingTools = null;
            }

            if (pendingRequest == null && messages.Count == 0)
            {
                if (DateTime.UtcNow < retryAfterUtc) return null;
                StartLoop(context, cycleDays, cycleBudget);
                SetStatus("started LLM incident cycle-planning loop");
                return null;
            }

            if (pendingRequest == null)
            {
                if (requestCount >= MaxRequestsPerLoop)
                {
                    FailLoop("request budget reached");
                    return null;
                }
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
                FailLoop("request failed: " + ex.Message);
                return null;
            }

            pendingRequest = null;
            if (!response.success)
            {
                LogDebug("LLM incident request failed: " + response.errorMessage);
                FailLoop("request failed: " + response.errorMessage);
                return null;
            }

            OrcaIncidentCyclePlan plan = HandleResponse(context, response, cycleDays, cycleBudget);
            if (pendingTools != null) return null;
            if (plan != null)
            {
                SetStatus("planned " + plan.incidents.Count + " scheduled incident(s)");
                failedLoops = 0; retryAfterUtc = default;
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
                FailLoop("request budget reached");
                return null;
            }

            StartRequest();
            return null;
        }

        private void StartLoop(AiToolContext context, float cycleDays, int cycleBudget)
        {
            policy = suppliedPolicy ?? OrcaPersonaBehaviors.Current.CreatePlanningPolicy(context, cycleDays, cycleBudget);
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
                + "Compute each running balance as previous balance + budgetDelta; compute the final balance from the deltas, never just label it 0. For example starting at 3, deltas -1,+1,-3 yield balances 2,3,0. Positive events require additional negative spending. "
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
                + (policy == null ? "Use at most 3 scheduled events unless the budget cannot be spent otherwise. " : policy.instructions)
                + "Place each event inside the cycle by offsetDays from 0 to "
                + cycleDays.ToString("F2")
                + ". "
                + "The final event must leave remainingBudget at 0."));

            StartRequest();
        }

        private void StartRequest()
        {
            RimAgentSettings settings = RimAgentMod.Settings;
            if (settings == null || !settings.HasConfiguredLlm)
            {
                Reset();
                return;
            }

            pendingRequest = client.SendChatCompletionWithToolsAsync(
                settings,
                new List<LlmChatMessage>(messages),
                LlmToolSchemas.BuildForRole(OrcaLlmModelRole.Decision),
                policy == null ? 1800 : policy.outputTokens,
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

                pendingTools = RunTools(context, response.toolCalls).GetEnumerator();

                return null;
            }

            if (!string.IsNullOrEmpty(response.content))
            {
                string rejectReason;
                OrcaIncidentCyclePlan plan = TryParseFinalCyclePlan(context, response.content, cycleDays, cycleBudget, out rejectReason);
                if (plan != null)
                {
                    if (policy == null || policy.Accepts(plan, out rejectReason)) return plan;
                }

                LogDebug("LLM incident planner returned final text without a valid cycle plan: " + rejectReason + " | " + response.content);
                SetStatus("final text without valid cycle plan: " + rejectReason);
                AddLog("Final text without valid cycle plan: " + rejectReason + " | " + response.content);
                if ((policy == null || policy.retryInvalidPlan) && requestCount < MaxRequestsPerLoop)
                {
                    messages.Add(LlmChatMessage.Assistant(response.content, null));
                    messages.Add(LlmChatMessage.User("Correct the plan and return only cyclePlan JSON. Validation error: " + rejectReason));
                    return null;
                }
            }

            FailLoop("invalid or empty final plan");
            return null;
        }

        private IEnumerable<bool> RunTools(AiToolContext context, List<LlmToolCall> calls)
            => new PlanningToolRound(this, context).Run(calls);

        private sealed class PlanningToolRound(LlmIncidentDecisionProvider owner, AiToolContext context)
            : RimAgent.Core.ToolRound<LlmToolCall, Task<AiToolResult>, AiToolResult>
        {
            private readonly AiToolSession session = new AiToolSession(context);
            protected override Task<AiToolResult> StartCall(LlmToolCall call)
            {
                var arguments = OrcaToolCallFormatter.ParseArguments(call.argumentsJson);
                owner.AddLog("Tool call: " + call.name + " " + FormatArguments(arguments));
                return owner.BeginTool(session, call.name, arguments);
            }
            protected override bool TryComplete(Task<AiToolResult> task, out AiToolResult result)
            {
                result = null;
                if (task != null && !task.IsCompleted) return false;
                try { result = task == null ? AiToolResult.Fail("tool returned no task") : task.GetAwaiter().GetResult() ?? AiToolResult.Fail("tool returned no result"); }
                catch (Exception ex) { result = AiToolResult.Fail(ex.GetType().Name + ": " + ex.Message); }
                return true;
            }
            protected override void AcceptResult(LlmToolCall call, Task<AiToolResult> task, AiToolResult result)
            {
                owner.AddLog("Tool result: " + (result.success ? "ok" : "failed") + " - " + result.message + FormatValues(result));
                owner.messages.Add(LlmChatMessage.Tool(call.id, SerializeToolResult(result)));
            }
        }

        private Task<AiToolResult> BeginTool(AiToolSession session, string toolName, Dictionary<string, string> arguments)
        {
            toolCallCount++;
            if (toolCallCount > MaxCyclePlanningToolCalls)
            {
                return Task.FromResult(AiToolResult.Fail("cycle planning tool budget reached; return the final cyclePlan JSON now"));
            }

            if (toolCallCount > MaxToolCalls)
            {
                return Task.FromResult(AiToolResult.Fail("tool call budget exceeded"));
            }

            if (toolName == "schedule_incident")
            {
                return Task.FromResult(AiToolResult.Fail("schedule_incident is disabled for cycle planning; return the final cyclePlan JSON instead"));
            }

            if (!LlmToolSchemas.IsToolAllowedForRole(OrcaLlmModelRole.Decision, toolName))
                return Task.FromResult(AiToolResult.Fail("tool is not available for storyteller planning"));

            return session.BeginInvoke(toolName, arguments);
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

        internal static OrcaIncidentCyclePlan TryParseFinalCyclePlan(
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
                    if (float.IsNaN(offsetDays) || float.IsInfinity(offsetDays) || offsetDays < 0f || offsetDays > cycleDays)
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
                    if (float.IsNaN(scheduled.pointsFactor) || float.IsInfinity(scheduled.pointsFactor))
                    {
                        rejectReason = "event " + i + " has non-finite pointsFactor";
                        return null;
                    }
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
            object raw;
            if (parsed.TryGetValue(key, out raw))
            {
                if (raw is double) return (float)(double)raw;
                if (raw is long) return (long)raw;
            }
            string text = GetString(parsed, key);
            if (text.NullOrEmpty())
            {
                return defaultValue;
            }

            float value;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : defaultValue;
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

        public void CancelPendingWork() { Reset(); retryAfterUtc = default; failedLoops = 0; }

        private void FailLoop(string reason)
        {
            Reset();
            failedLoops = Math.Min(failedLoops + 1, 5);
            retryAfterUtc = DateTime.UtcNow.AddSeconds(30 * (1 << (failedLoops - 1)));
            SetStatus(reason + "; retry after " + retryAfterUtc.ToString("O"));
            AddLog(lastStatus);
            LogDebug(lastStatus);
        }

        private void Reset()
        {
            client.CancelQueuedRequests();
            if (pendingTools != null) pendingTools.Dispose();
            pendingTools = null;
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
                RimAgentSettings settings = RimAgentMod.Settings;
                return settings == null ? 8 : settings.maxToolCalls;
            }
        }

        private static int MaxCyclePlanningToolCalls
        {
            get { return 2; }
        }

        private static void LogDebug(string message)
        {
            if (RimAgentMod.Settings != null && RimAgentMod.Settings.debugLogging)
            {
                Log.Message("[RimAgent] " + message);
            }
        }
    }
}
