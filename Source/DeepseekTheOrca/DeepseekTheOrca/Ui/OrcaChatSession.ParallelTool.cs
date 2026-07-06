using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace DeepseekTheOrca
{
    public sealed partial class OrcaChatSession
    {
        private void ResetParallelToolState()
        {
            pendingParallelToolRequest = null;
            parallelToolMessages = null;
            parallelToolInstruction = "";
            parallelToolRoundsUsed = 0;
            parallelToolExecutionSucceeded = false;
            parallelToolResultSummaries.Clear();
        }

        private bool TryStartParallelToolExecution(DeepseekTheOrcaSettings settings, string instruction, string source)
        {
            if (instruction.NullOrEmpty() || !allowExecutionToolsThisTurn)
            {
                return false;
            }
            if (settings == null || !settings.HasModelForRole(OrcaLlmModelRole.Tool))
            {
                AddProcess("Parallel tool execution requested, but no tool model is configured.");
                return false;
            }
            if (pendingParallelToolRequest != null)
            {
                AddProcess("Parallel tool execution requested, but another parallel tool branch is already running.");
                return false;
            }

            parallelToolInstruction = instruction.Trim();
            parallelToolRoundsUsed = 0;
            parallelToolExecutionSucceeded = false;
            parallelToolResultSummaries.Clear();
            parallelToolMessages = SnapshotMessagesForRole(OrcaLlmModelRole.Tool);
            parallelToolMessages.Add(LlmChatMessage.System(ParallelToolInstructionMessage(parallelToolInstruction)));
            AddProcess("Parallel tool execution requested by " + source + ": " + parallelToolInstruction);
            StartNextParallelToolRequest(settings, "initial parallel execution request");
            return true;
        }

        private bool CanStartParallelToolExecution(DeepseekTheOrcaSettings settings, OrcaControllerDecision decision)
        {
            return decision != null
                && decision.HasParallelToolInstruction
                && allowExecutionToolsThisTurn
                && settings != null
                && settings.HasModelForRole(OrcaLlmModelRole.Tool);
        }

        private void StartNextParallelToolRequest(DeepseekTheOrcaSettings settings, string reason)
        {
            HashSet<string> allowedToolNames = AllowedToolNamesForToolRequest(includeExecutionPlanningTools: true, queryText: parallelToolInstruction);
            AddProcess("Parallel tool branch selected " + allowedToolNames.Count + " tool schema(s): " + string.Join(", ", allowedToolNames.ToArray()));
            pendingParallelToolRequest = client.SendChatCompletionWithToolsAsync(
                settings,
                parallelToolMessages,
                LlmToolSchemas.BuildForRole(OrcaLlmModelRole.Tool, allowedToolNames),
                700,
                0.45f,
                OrcaLlmModelRole.Tool);
            statusText = "DTO_OrcaChatUsingTools".Translate();
            NotifyAgentPhase(OrcaAgentPhase.ToolGathering, OrcaLlmModelRole.Tool, false, reason);
        }

        private void TickParallelToolRequest()
        {
            if (pendingParallelToolRequest == null || !pendingParallelToolRequest.IsCompleted)
            {
                return;
            }

            LlmChatResponse response;
            try
            {
                response = pendingParallelToolRequest.Result;
            }
            catch (Exception ex)
            {
                pendingParallelToolRequest = null;
                AddProcess("Parallel tool branch failed: " + ex.GetType().Name + ": " + ex.Message);
                FinishParallelToolExecution("parallel tool branch failed");
                return;
            }

            pendingParallelToolRequest = null;
            if (response == null || !response.success)
            {
                AddProcess("Parallel tool branch failed: " + (response == null ? "empty response" : response.errorMessage));
                FinishParallelToolExecution("parallel tool branch failed");
                return;
            }

            if (response.toolCalls == null || response.toolCalls.Count == 0)
            {
                AddProcess("Parallel tool branch produced no tool calls; no event execution occurred.");
                parallelToolMessages.Add(LlmChatMessage.Assistant(response.content, null));
                FinishParallelToolExecution("parallel tool branch produced no tool calls");
                return;
            }

            HandleParallelToolCalls(response);
        }

        private void HandleParallelToolCalls(LlmChatResponse response)
        {
            bool roundBudgetExhausted = parallelToolRoundsUsed >= MaxToolGatheringRounds + 1;
            bool finalExecutionOnly = roundBudgetExhausted && ContainsExecutionToolCall(response);
            if (roundBudgetExhausted && !finalExecutionOnly)
            {
                AddProcess("Parallel tool branch budget exhausted before executing requested tools.");
                FinishParallelToolExecution("parallel tool budget exhausted");
                return;
            }

            if (finalExecutionOnly)
            {
                AddProcess("Parallel tool branch round budget is exhausted; allowing final execution tool call only.");
            }
            else
            {
                parallelToolRoundsUsed++;
            }
            AddProcess("Parallel tool branch received " + response.toolCalls.Count + " tool call(s), round " + parallelToolRoundsUsed + ".");
            parallelToolMessages.Add(LlmChatMessage.Assistant(response.content, response.toolCalls));
            bool toolCallBudgetExhausted = false;

            for (int i = 0; i < response.toolCalls.Count; i++)
            {
                LlmToolCall toolCall = response.toolCalls[i];
                if (finalExecutionOnly && !AiStoryToolRegistry.IsExecutionTool(toolCall.name))
                {
                    AddProcess("Parallel tool call skipped after round budget exhaustion because it is not an execution tool: " + toolCall.name);
                    continue;
                }

                Dictionary<string, string> arguments = OrcaToolCallFormatter.ParseArguments(toolCall.argumentsJson);
                AddProcess("Parallel tool call: " + toolCall.name + " " + OrcaToolCallFormatter.FormatArguments(arguments));
                OrcaChatToolExecution execution;
                if (!TryReserveToolCall())
                {
                    toolCallBudgetExhausted = true;
                    execution = new OrcaChatToolExecution();
                    execution.result = AiToolResult.Fail("tool call budget exhausted; no further parallel tools were executed");
                    execution.ProcessLines.Add("Parallel tool call skipped because maxToolCalls budget was exhausted.");
                }
                else
                {
                    execution = OrcaChatToolExecutor.Execute(this, toolCall, arguments, OrcaLlmModelRole.Tool, allowExecutionToolsThisTurn);
                }

                AddProcessLines(execution.ProcessLines);
                AiToolResult result = execution.result;
                string resultLine = (result.success ? "ok" : "failed") + " - " + result.message + OrcaToolResultFormatter.FormatValues(result);
                AddProcess("Parallel tool result: " + resultLine);
                parallelToolResultSummaries.Add(toolCall.name + ": " + resultLine);
                totalToolCalls++;
                lastToolName = toolCall.name;
                lastToolResult = resultLine;
                if (!result.success)
                {
                    failedToolCalls++;
                }

                if (execution.exposedToChat)
                {
                    OrcaSessionMemory.Add("tool_" + toolCall.name, OrcaToolResultFormatter.MemoryText(result));
                }
                if (result.success && AiStoryToolRegistry.IsExecutionTool(toolCall.name))
                {
                    parallelToolExecutionSucceeded = true;
                }

                parallelToolMessages.Add(LlmChatMessage.Tool(toolCall.id, OrcaToolResultFormatter.Serialize(result)));
            }

            if (parallelToolExecutionSucceeded)
            {
                FinishParallelToolExecution("parallel execution tool succeeded");
                return;
            }
            if (toolCallBudgetExhausted || toolCallsUsedThisTurn >= MaxToolCallsForSettings(DeepseekTheOrcaMod.Settings))
            {
                FinishParallelToolExecution("parallel tool call budget exhausted");
                return;
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !settings.HasModelForRole(OrcaLlmModelRole.Tool))
            {
                FinishParallelToolExecution("tool model unavailable after parallel tool results");
                return;
            }

            parallelToolMessages.Add(LlmChatMessage.System(
                "Parallel tool results are available. If the requested in-game event or game-state change has not been executed yet and enough information is now available, call the final execution tool now. "
                + "If execution is impossible, return no tool calls. Do not write player-facing prose."));
            StartNextParallelToolRequest(settings, "parallel tool results supplied");
        }

        private static bool ContainsExecutionToolCall(LlmChatResponse response)
        {
            if (response == null || response.toolCalls == null)
            {
                return false;
            }

            for (int i = 0; i < response.toolCalls.Count; i++)
            {
                LlmToolCall toolCall = response.toolCalls[i];
                if (toolCall != null && AiStoryToolRegistry.IsExecutionTool(toolCall.name))
                {
                    return true;
                }
            }

            return false;
        }

        private void FinishParallelToolExecution(string reason)
        {
            if (parallelToolResultSummaries.Count > 0)
            {
                transcript.AddMessage(LlmChatMessage.System(
                    "Parallel tool execution branch completed. Reason: " + reason + ".\n"
                    + "Controller tool instruction: " + parallelToolInstruction + "\n"
                    + "Results:\n- " + string.Join("\n- ", parallelToolResultSummaries.ToArray())));
                transcript.MarkChanged();
            }

            pendingParallelToolRequest = null;
            parallelToolMessages = null;
            parallelToolInstruction = "";
            parallelToolRoundsUsed = 0;
            parallelToolExecutionSucceeded = false;
            parallelToolResultSummaries.Clear();
            AddProcess("Parallel tool branch finished: " + reason + ".");
            CompleteTurnIfIdle(reason);
        }

        private static string ParallelToolInstructionMessage(string instruction)
        {
            return "Parallel tool execution branch. The controller has routed the visible player-facing reply to the dialogue model. "
                + "Your job is only to perform the concrete in-game event or game-state change requested below through available tools. "
                + "Do not write player-facing prose. Do not narrate. Do not ask the player a question. "
                + "If supporting data is required, call only the minimal lookup or validation tools first, then call the final execution tool such as schedule_incident, trigger_raid, or spawn_pawns. Do not spend tool rounds on optional flavor data. "
                + "Stop after a successful execution tool call. If execution is impossible or unsafe under the tool validators, return no tool calls after the failure is evident.\n\n"
                + "Controller tool instruction:\n" + (instruction ?? "").Trim();
        }
    }
}
