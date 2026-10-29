using RimAgent.Core;
using Verse;

namespace RimAgent;

public sealed partial class OrcaChatSession
{
    private abstract class ChatToolRoundBase(OrcaChatSession session, LlmChatResponse response)
        : ToolRound<LlmToolCall, OrcaChatToolExecution, AiToolResult>
    {
        protected readonly OrcaChatSession Session = session;
        protected readonly LlmChatResponse Response = response;
        protected bool BudgetExhausted;
        protected abstract string Prefix { get; }
        protected abstract OrcaLlmModelRole Role { get; }
        protected override OrcaChatToolExecution StartCall(LlmToolCall call)
        {
            var arguments = OrcaToolCallFormatter.ParseArguments(call.argumentsJson);
            Session.AddProcess(Prefix + "tool call: " + call.name + " " + OrcaToolCallFormatter.FormatArguments(arguments));
            if (Session.TryReserveToolCall())
                return OrcaChatToolExecutor.Execute(Session, call, arguments, Role, Session.allowExecutionToolsThisTurn);
            BudgetExhausted = true;
            var operation = new OrcaChatToolExecution { result = AiToolResult.Fail("tool call budget exhausted; no further tools were executed") };
            operation.ProcessLines.Add(Prefix + "tool call skipped because maxToolCalls budget was exhausted.");
            return operation;
        }
        protected override bool TryComplete(OrcaChatToolExecution operation, out AiToolResult result)
        {
            var done = operation.TryComplete(); result = operation.result; return done;
        }
        protected override void AcceptResult(LlmToolCall call, OrcaChatToolExecution operation, AiToolResult result)
        {
            Session.AddProcessLines(operation.ProcessLines);
            string line = (result.success ? "ok" : "failed") + " - " + result.message;
            string detailed = line + OrcaToolResultFormatter.FormatValues(result);
            Session.AddProcess(Prefix + "tool result: " + detailed);
            Session.totalToolCalls++;
            Session.lastToolName = call.name;
            Session.lastToolResult = LastResult(line, detailed);
            if (!result.success) Session.failedToolCalls++;
            if (operation.exposedToChat) OrcaSessionMemory.Add("tool_" + call.name, OrcaToolResultFormatter.MemoryText(result));
            StoreResult(call, result, detailed);
        }
        protected virtual string LastResult(string line, string detailed) => line;
        protected abstract void StoreResult(LlmToolCall call, AiToolResult result, string detailed);
    }

    private sealed class ConversationToolRound(OrcaChatSession session, LlmChatResponse response) : ChatToolRoundBase(session, response)
    {
        protected override string Prefix => "";
        protected override OrcaLlmModelRole Role => Session.pendingRequestRole;
        protected override bool BeginRound()
        {
            if (Session.toolRoundsUsed >= MaxToolRounds)
            {
                Session.ContinueToDialogueWithToolBudgetExhausted("hard tool round budget reached before executing requested tools"); return false;
            }
            Session.toolRoundsUsed++;
            Session.statusText = "DTO_OrcaChatUsingTools".Translate();
            Session.AddProcess("Received " + Response.toolCalls.Count + " tool call(s), round " + Session.toolRoundsUsed + ".");
            Session.transcript.AddMessage(LlmChatMessage.Assistant(Response.content, Response.toolCalls));
            return true;
        }
        protected override void StoreResult(LlmToolCall call, AiToolResult result, string detailed)
            => Session.transcript.AddMessage(LlmChatMessage.Tool(call.id, OrcaToolResultFormatter.Serialize(result)));
        protected override void FinishRound()
        {
            var settings = RimAgentMod.Settings;
            if (settings == null) { Session.statusText = "DTO_OrcaChatNoApiKey".Translate(); Session.SetError(Session.statusText); return; }
            if (BudgetExhausted || !Session.CanContinueSpecialistGathering(settings))
            {
                Session.ContinueToDialogueWithToolBudgetExhausted(BudgetExhausted ? "tool call budget exhausted after executing requested tools" : "tool gathering round budget exhausted after tool results"); return;
            }
            Session.specialistReturnedNoToolCalls = false;
            Session.transcript.AddMessage(LlmChatMessage.System("Specialist tool results have been supplied. Return control to the controller model so it can decide whether to gather more data or route to dialogue."));
            Session.StartControllerReviewOrDialogue(settings, "tool results supplied");
        }
    }

    private sealed class ParallelChatToolRound(OrcaChatSession session, LlmChatResponse response) : ChatToolRoundBase(session, response)
    {
        private bool finalExecutionOnly;
        protected override string Prefix => "Parallel ";
        protected override OrcaLlmModelRole Role => OrcaLlmModelRole.Tool;
        protected override bool BeginRound()
        {
            bool exhausted = Session.parallelToolRoundsUsed >= MaxToolGatheringRounds + 1;
            finalExecutionOnly = exhausted && ContainsExecutionToolCall(Response);
            if (exhausted && !finalExecutionOnly)
            {
                Session.AddProcess("Parallel tool branch budget exhausted before executing requested tools.");
                Session.FinishParallelToolExecution("parallel tool budget exhausted"); return false;
            }
            if (finalExecutionOnly) Session.AddProcess("Parallel tool branch round budget is exhausted; allowing final execution tool call only.");
            else Session.parallelToolRoundsUsed++;
            Session.AddProcess("Parallel tool branch received " + Response.toolCalls.Count + " tool call(s), round " + Session.parallelToolRoundsUsed + ".");
            Session.parallelToolMessages.Add(LlmChatMessage.Assistant(Response.content, Response.toolCalls)); return true;
        }
        protected override bool ShouldRun(LlmToolCall call)
        {
            if (!finalExecutionOnly || AiStoryToolRegistry.IsExecutionTool(call.name)) return true;
            Session.AddProcess("Parallel tool call skipped after round budget exhaustion because it is not an execution tool: " + call.name); return false;
        }
        protected override string LastResult(string line, string detailed) => detailed;
        protected override void StoreResult(LlmToolCall call, AiToolResult result, string detailed)
        {
            Session.parallelToolResultSummaries.Add(call.name + ": " + detailed);
            if (result.success && AiStoryToolRegistry.IsExecutionTool(call.name)) Session.parallelToolExecutionSucceeded = true;
            Session.parallelToolMessages.Add(LlmChatMessage.Tool(call.id, OrcaToolResultFormatter.Serialize(result)));
        }
        protected override void FinishRound()
        {
            if (Session.parallelToolExecutionSucceeded) { Session.FinishParallelToolExecution("parallel execution tool succeeded"); return; }
            if (BudgetExhausted || Session.toolCallsUsedThisTurn >= Session.MaxToolCallsForSettings(RimAgentMod.Settings))
            { Session.FinishParallelToolExecution("parallel tool call budget exhausted"); return; }
            var settings = RimAgentMod.Settings;
            if (settings == null || !settings.HasModelForRole(OrcaLlmModelRole.Tool))
            { Session.FinishParallelToolExecution("tool model unavailable after parallel tool results"); return; }
            Session.parallelToolMessages.Add(LlmChatMessage.System("Parallel tool results are available. If the requested in-game event or game-state change has not been executed yet and enough information is now available, call the final execution tool now. If execution is impossible, return no tool calls. Do not write player-facing prose."));
            Session.StartNextParallelToolRequest(settings, "parallel tool results supplied");
        }
    }
}
