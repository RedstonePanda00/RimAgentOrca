using System.Collections.Generic;
using System;
using System.Threading.Tasks;
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

            if (!string.IsNullOrEmpty(OrcaPersonaBehaviors.Current.ExecutionBlockReason) && AiStoryToolRegistry.IsExecutionTool(toolName))
            {
                execution.result = AiToolResult.Fail(OrcaPersonaBehaviors.Current.ExecutionBlockReason);
            }
            else if (!IsToolExposedToChat(toolName))
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
            else if (AiStoryToolRegistry.IsExecutionTool(toolName))
            {
                execution.result = InvokeExecutionToolFromChat(chatSession, session, context, toolName, arguments, execution.ProcessLines);
            }
            else
            {
                var worker = AiStoryToolRegistry.WorkerFor(toolName);
                var asyncWorker = worker as IOrcaAsyncToolWorker;
                string reason;
                try
                {
                    if (asyncWorker != null)
                    {
                        if (!worker.CanUse(context, out reason)) execution.result = AiToolResult.Fail(reason);
                        else execution.pending = asyncWorker.BeginInvoke(context, new Dictionary<string, string>(arguments ?? new Dictionary<string, string>()));
                    }
                    else if (OrcaHttpMcpClient.IsExposedTool(toolName))
                        execution.pending = OrcaHttpMcpClient.BeginInvokeExposedTool(toolName, arguments);
                    else execution.result = session.Invoke(toolName, arguments);
                    if (execution.pending == null && execution.result == null) execution.result = AiToolResult.Fail("tool returned no operation");
                }
                catch (Exception ex) { execution.result = AiToolResult.Fail(ex.GetType().Name + ": " + ex.Message); }
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

        private static AiToolResult InvokeExecutionToolFromChat(
            OrcaChatSession chatSession,
            AiToolSession session,
            AiToolContext context,
            string toolName,
            Dictionary<string, string> arguments,
            List<string> processLines)
        {
            AiToolResult validationResult = session.Invoke(toolName, arguments);
            if (!validationResult.success)
            {
                return validationResult;
            }

            AiToolResult gateResult;
            if (!ExtensionAllowsExecutionTool(chatSession, toolName, arguments, processLines, out gateResult))
            {
                return gateResult;
            }

            OrcaToolWorker worker = AiStoryToolRegistry.WorkerFor(toolName);
            if (worker == null)
            {
                return AiToolResult.Fail("execution tool worker is unavailable: " + toolName);
            }

            return worker.ExecuteValidated(context, arguments, processLines);
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
                case OrcaLlmModelRole.Embedding:
                    return "embedding";
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
        internal Task<AiToolResult> pending;

        public bool TryComplete()
        {
            if (pending == null) return true;
            if (!pending.IsCompleted) return false;
            try { result = pending.GetAwaiter().GetResult() ?? AiToolResult.Fail("tool returned no result"); }
            catch (Exception ex) { result = AiToolResult.Fail(ex.GetType().Name + ": " + ex.Message); }
            pending = null;
            return true;
        }
    }
}
