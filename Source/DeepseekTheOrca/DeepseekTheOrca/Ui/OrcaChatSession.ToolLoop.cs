using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
    public sealed partial class OrcaChatSession
    {
        private void HandleToolCalls(LlmChatResponse response)
        {
            if (toolRoundsUsed >= MaxToolRounds)
            {
                ContinueToDialogueWithToolBudgetExhausted("hard tool round budget reached before executing requested tools");
                return;
            }

            toolRoundsUsed++;
            statusText = "DTO_OrcaChatUsingTools".Translate();
            AddProcess("Received " + response.toolCalls.Count + " tool call(s), round " + toolRoundsUsed + ".");
            messages.Add(LlmChatMessage.Assistant(response.content, response.toolCalls));
            bool toolCallBudgetExhausted = false;

            for (int i = 0; i < response.toolCalls.Count; i++)
            {
                LlmToolCall toolCall = response.toolCalls[i];
                Dictionary<string, string> arguments = OrcaToolCallFormatter.ParseArguments(toolCall.argumentsJson);
                AddProcess("Tool call: " + toolCall.name + " " + OrcaToolCallFormatter.FormatArguments(arguments));
                OrcaChatToolExecution execution;
                if (!TryReserveToolCall())
                {
                    toolCallBudgetExhausted = true;
                    execution = new OrcaChatToolExecution();
                    execution.result = AiToolResult.Fail("tool call budget exhausted; no further tools were executed");
                    execution.ProcessLines.Add("Tool call skipped because maxToolCalls budget was exhausted.");
                }
                else
                {
                    execution = OrcaChatToolExecutor.Execute(this, toolCall, arguments, pendingRequestRole, allowExecutionToolsThisTurn);
                }
                AddProcessLines(execution.ProcessLines);
                AiToolResult result = execution.result;

                AddProcess("Tool result: " + (result.success ? "ok" : "failed") + " - " + result.message + OrcaToolResultFormatter.FormatValues(result));
                totalToolCalls++;
                lastToolName = toolCall.name;
                lastToolResult = (result.success ? "ok" : "failed") + " - " + result.message;
                if (!result.success)
                {
                    failedToolCalls++;
                }

                if (execution.exposedToChat)
                {
                    OrcaSessionMemory.Add("tool_" + toolCall.name, OrcaToolResultFormatter.MemoryText(result));
                }
                messages.Add(LlmChatMessage.Tool(toolCall.id, OrcaToolResultFormatter.Serialize(result)));
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null)
            {
                statusText = "DTO_OrcaChatNoApiKey".Translate();
                SetError(statusText);
                return;
            }

            if (toolCallBudgetExhausted || !CanContinueSpecialistGathering(settings))
            {
                ContinueToDialogueWithToolBudgetExhausted(toolCallBudgetExhausted ? "tool call budget exhausted after executing requested tools" : "tool gathering round budget exhausted after tool results");
                return;
            }

            specialistReturnedNoToolCalls = false;
            messages.Add(LlmChatMessage.System(
                "Specialist tool results have been supplied. Return control to the controller model so it can decide whether to gather more data or route to dialogue."));
            StartControllerReviewOrDialogue(settings, "tool results supplied");
        }

        private bool TryReserveToolCall()
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            int maxCalls = MaxToolCallsForSettings(settings);
            if (toolCallsUsedThisTurn >= maxCalls)
            {
                return false;
            }

            toolCallsUsedThisTurn++;
            return true;
        }

        private void ContinueToDialogueWithToolBudgetExhausted(string reason)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !settings.HasModelForRole(OrcaLlmModelRole.Dialogue))
            {
                statusText = "DTO_OrcaChatNoApiKey".Translate();
                SetError(statusText);
                return;
            }

            AddProcess("Tool budget exhausted; routing to dialogue model with existing tool results. Reason: " + reason + ".");
            messages.Add(LlmChatMessage.System(
                "Tool budget is exhausted. Use only the existing conversation and tool results already supplied. "
                + "Do not request or call more tools. The next assistant response must be exactly one JSON object and no extra text. "
                + "JSON schema: " + OrcaChatPromptBuilder.ChatReplyJsonSchema() + "."));
            ForceNextModelRole(OrcaLlmModelRole.Dialogue);
            StartRequest(settings);
        }
    }
}
