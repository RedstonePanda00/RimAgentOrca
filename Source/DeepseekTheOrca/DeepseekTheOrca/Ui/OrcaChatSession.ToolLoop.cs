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
                statusText = "DTO_OrcaChatToolBudgetReached".Translate();
                SetError(statusText);
                return;
            }

            toolRoundsUsed++;
            statusText = "DTO_OrcaChatUsingTools".Translate();
            AddProcess("Received " + response.toolCalls.Count + " tool call(s), round " + toolRoundsUsed + ".");
            messages.Add(LlmChatMessage.Assistant(response.content, response.toolCalls));

            for (int i = 0; i < response.toolCalls.Count; i++)
            {
                LlmToolCall toolCall = response.toolCalls[i];
                Dictionary<string, string> arguments = OrcaToolCallFormatter.ParseArguments(toolCall.argumentsJson);
                AddProcess("Tool call: " + toolCall.name + " " + OrcaToolCallFormatter.FormatArguments(arguments));
                OrcaChatToolExecution execution = OrcaChatToolExecutor.Execute(this, toolCall, arguments, pendingRequestRole, allowExecutionToolsThisTurn);
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
            OrcaLlmModelRole nextRole = NextRoleAfterToolResults(settings);
            if (settings == null || !settings.HasModelForRole(nextRole))
            {
                statusText = "DTO_OrcaChatNoApiKey".Translate();
                SetError(statusText);
                return;
            }

            if (nextRole == OrcaLlmModelRole.Tool || nextRole == OrcaLlmModelRole.WebSearch)
            {
                messages.Add(LlmChatMessage.System(
                    "Tool results have been supplied. If more game data is needed to satisfy the player's request, call another tool. "
                    + "If enough information has been gathered, do not call tools; the dialogue model will write the final player-facing response."));
            }
            else
            {
                messages.Add(LlmChatMessage.System(
                    "Tool results have been supplied. The next assistant response must be exactly one JSON object and no extra text. "
                    + "JSON schema: " + OrcaChatPromptBuilder.ChatReplyJsonSchema() + "."));
            }

            ForceNextModelRole(nextRole);
            StartRequest(settings);
        }

        private OrcaLlmModelRole NextRoleAfterToolResults(DeepseekTheOrcaSettings settings)
        {
            if (toolRoundsUsed >= MaxToolGatheringRounds || settings == null)
            {
                return OrcaLlmModelRole.Dialogue;
            }

            if (pendingRequestRole == OrcaLlmModelRole.WebSearch && settings.HasModelForRole(OrcaLlmModelRole.WebSearch))
            {
                return OrcaLlmModelRole.WebSearch;
            }

            if (pendingRequestRole == OrcaLlmModelRole.Tool && settings.HasModelForRole(OrcaLlmModelRole.Tool))
            {
                return OrcaLlmModelRole.Tool;
            }

            return OrcaLlmModelRole.Dialogue;
        }
    }
}
