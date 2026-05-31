using System.Collections.Generic;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaChatToolExecutor
    {
        public static OrcaChatToolExecution Execute(
            OrcaChatSession chatSession,
            LlmToolCall toolCall,
            Dictionary<string, string> arguments,
            OrcaLlmModelRole modelRole,
            bool allowExecutionToolsThisTurn)
        {
            OrcaChatToolExecution execution = new OrcaChatToolExecution();
            string toolName = toolCall == null ? "" : toolCall.name;
            AiToolContext context = new AiToolContext(Find.CurrentMap, null, null);
            AiToolSession session = new AiToolSession(context);

            if (!IsToolExposedToChat(toolName))
            {
                execution.result = AiToolResult.Fail("tool is not exposed to chat: " + toolName);
            }
            else if (!LlmToolSchemas.IsToolAllowedForRole(modelRole, toolName))
            {
                execution.result = AiToolResult.Fail("tool is not available to " + ModelRoleLabel(modelRole) + " model: " + toolName);
            }
            else if (toolName == "web_search" && (DeepseekTheOrcaMod.Settings == null || !DeepseekTheOrcaMod.Settings.UsesLocalWebSearchTool))
            {
                execution.result = AiToolResult.Fail("web search is disabled in mod settings");
            }
            else if (Find.CurrentMap == null && ToolRequiresCurrentMap(toolName))
            {
                execution.result = AiToolResult.Fail("no current map");
            }
            else if (!allowExecutionToolsThisTurn && !ToolAllowsDuringProactive(toolName))
            {
                execution.result = AiToolResult.Fail("tool is disabled for proactive trigger turns");
            }
            else if (toolName == "schedule_incident")
            {
                execution.result = InvokeScheduleIncidentFromChat(chatSession, session, arguments, execution.ProcessLines);
            }
            else if (toolName == "trigger_raid")
            {
                execution.result = InvokeTriggerRaidFromChat(chatSession, session, arguments, execution.ProcessLines);
            }
            else if (toolName == "spawn_pawns")
            {
                execution.result = InvokeSpawnPawnsFromChat(chatSession, session, arguments, execution.ProcessLines);
            }
            else
            {
                execution.result = session.Invoke(toolName, arguments);
            }

            execution.exposedToChat = IsToolExposedToChat(toolName);
            return execution;
        }

        public static bool IsToolExposedToChat(string toolName)
        {
            if (toolName == "web_search" && (DeepseekTheOrcaMod.Settings == null || !DeepseekTheOrcaMod.Settings.UsesLocalWebSearchTool))
            {
                return false;
            }

            return AiStoryToolRegistry.IsExposedToChat(toolName) || OrcaHttpMcpClient.IsExposedTool(toolName);
        }

        private static AiToolResult InvokeScheduleIncidentFromChat(
            OrcaChatSession chatSession,
            AiToolSession session,
            Dictionary<string, string> arguments,
            List<string> processLines)
        {
            AiToolResult validationResult = session.Invoke("schedule_incident", arguments);
            if (!validationResult.success)
            {
                return validationResult;
            }

            AiToolResult gateResult;
            if (!ExtensionAllowsExecutionTool(chatSession, "schedule_incident", arguments, processLines, out gateResult))
            {
                return gateResult;
            }

            StorytellerComp_DeepseekOrca comp = ActiveOrcaComp();
            if (comp == null)
            {
                return AiToolResult.Fail("active storyteller does not contain StorytellerComp_DeepseekOrca");
            }

            AiIncidentPlan plan;
            string rejectReason;
            if (!TryBuildPlan(arguments, out plan, out rejectReason))
            {
                return AiToolResult.Fail(rejectReason);
            }

            string message;
            string traceText;
            bool fired = comp.TryFireIncidentNowForDebug(Find.CurrentMap, plan, out message, out traceText);
            if (!fired)
            {
                return AiToolResult.Fail(message);
            }

            return AiToolResult.Ok(message)
                .WithValue("incidentDef", plan.incidentDefName)
                .WithValue("reason", plan.reason ?? "");
        }

        private static AiToolResult InvokeTriggerRaidFromChat(
            OrcaChatSession chatSession,
            AiToolSession session,
            Dictionary<string, string> arguments,
            List<string> processLines)
        {
            AiToolResult validationResult = session.Invoke("trigger_raid", arguments);
            if (!validationResult.success)
            {
                return validationResult;
            }

            AiToolResult gateResult;
            if (!ExtensionAllowsExecutionTool(chatSession, "trigger_raid", arguments, processLines, out gateResult))
            {
                return gateResult;
            }

            StorytellerComp_DeepseekOrca comp = ActiveOrcaComp();
            if (comp == null)
            {
                return AiToolResult.Fail("active storyteller does not contain StorytellerComp_DeepseekOrca");
            }

            string message;
            string traceText;
            bool fired = comp.TryFireRaidNowForDebug(Find.CurrentMap, arguments, out message, out traceText);
            if (!traceText.NullOrEmpty())
            {
                processLines.Add("Trigger raid trace: " + traceText.Replace("\n", " | "));
            }
            if (!fired)
            {
                return AiToolResult.Fail(message);
            }

            return AiToolResult.Ok(message);
        }

        private static AiToolResult InvokeSpawnPawnsFromChat(
            OrcaChatSession chatSession,
            AiToolSession session,
            Dictionary<string, string> arguments,
            List<string> processLines)
        {
            AiToolResult validationResult = session.Invoke("spawn_pawns", arguments);
            if (!validationResult.success)
            {
                return validationResult;
            }

            AiToolResult gateResult;
            if (!ExtensionAllowsExecutionTool(chatSession, "spawn_pawns", arguments, processLines, out gateResult))
            {
                return gateResult;
            }

            AiToolContext context = new AiToolContext(Find.CurrentMap, null, null);
            string message;
            bool spawned = OrcaPawnSpawnUtility.TrySpawnPawns(context, arguments, out message);
            if (!spawned)
            {
                return AiToolResult.Fail(message);
            }

            return AiToolResult.Ok(message);
        }

        private static bool ToolAllowsDuringProactive(string toolName)
        {
            return OrcaHttpMcpClient.IsExposedTool(toolName) || AiStoryToolRegistry.AllowsDuringProactive(toolName);
        }

        private static bool ToolRequiresCurrentMap(string toolName)
        {
            return !OrcaHttpMcpClient.IsExposedTool(toolName) && AiStoryToolRegistry.RequiresCurrentMap(toolName);
        }

        private static bool ExtensionAllowsExecutionTool(
            OrcaChatSession chatSession,
            string toolName,
            Dictionary<string, string> arguments,
            List<string> processLines,
            out AiToolResult result)
        {
            OrcaExecutionGateContext gateContext = new OrcaExecutionGateContext(chatSession, toolName, arguments);
            OrcaExtensionManager.EvaluateExecutionTool(gateContext);
            for (int i = 0; i < gateContext.ProcessLines.Count; i++)
            {
                processLines.Add(gateContext.ProcessLines[i]);
            }

            if (gateContext.Blocked)
            {
                result = AiToolResult.Fail(gateContext.BlockReason);
                return false;
            }

            result = null;
            return true;
        }

        private static StorytellerComp_DeepseekOrca ActiveOrcaComp()
        {
            if (Find.Storyteller == null || Find.Storyteller.storytellerComps == null)
            {
                return null;
            }

            for (int i = 0; i < Find.Storyteller.storytellerComps.Count; i++)
            {
                StorytellerComp_DeepseekOrca comp = Find.Storyteller.storytellerComps[i] as StorytellerComp_DeepseekOrca;
                if (comp != null)
                {
                    return comp;
                }
            }

            return null;
        }

        private static bool TryBuildPlan(Dictionary<string, string> arguments, out AiIncidentPlan plan, out string rejectReason)
        {
            plan = null;
            rejectReason = null;

            string incidentDef;
            if (arguments == null || !arguments.TryGetValue("incidentDef", out incidentDef) || incidentDef.NullOrEmpty())
            {
                rejectReason = "missing argument: incidentDef";
                return false;
            }

            float pointsFactor = 1f;
            string pointsFactorText;
            if (arguments.TryGetValue("pointsFactor", out pointsFactorText))
            {
                float.TryParse(pointsFactorText, out pointsFactor);
            }

            string reason;
            arguments.TryGetValue("reason", out reason);
            plan = AiIncidentPlan.For(incidentDef, reason ?? "The chat agent selected this incident.", pointsFactor);
            return true;
        }

        private static string ModelRoleLabel(OrcaLlmModelRole role)
        {
            switch (role)
            {
                case OrcaLlmModelRole.Decision:
                    return "decision";
                case OrcaLlmModelRole.Controller:
                    return "controller";
                case OrcaLlmModelRole.Dialogue:
                    return "dialogue";
                case OrcaLlmModelRole.Tool:
                    return "tool";
                case OrcaLlmModelRole.Vision:
                    return "vision";
                case OrcaLlmModelRole.WebSearch:
                    return "web-search";
                default:
                    return "fallback";
            }
        }
    }

    public sealed class OrcaChatToolExecution
    {
        public AiToolResult result;
        public bool exposedToChat;
        public readonly List<string> ProcessLines = new List<string>();
    }
}
