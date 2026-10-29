using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimAgent
{
    public sealed partial class OrcaChatSession
    {
        private void PumpToolBatch(ref IEnumerator<bool> batch)
        {
            if (batch == null) return;
            try { if (batch.MoveNext()) return; }
            catch (System.Exception ex) { SetError("Tool workflow failed: " + ex.Message); }
            batch.Dispose();
            batch = null;
            CompleteTurnIfIdle("tool batch completed");
        }

        private void HandleToolCalls(LlmChatResponse response)
        {
            pendingToolBatch = RunHandleToolCalls(response).GetEnumerator();
            PumpToolBatch(ref pendingToolBatch);
        }

        private IEnumerable<bool> RunHandleToolCalls(LlmChatResponse response)
            => new ConversationToolRound(this, response).Run(response.toolCalls);

        private bool TryReserveToolCall()
        {
            RimAgentSettings settings = RimAgentMod.Settings;
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
            RimAgentSettings settings = RimAgentMod.Settings;
            if (settings == null || !settings.HasModelForRole(OrcaLlmModelRole.Dialogue))
            {
                statusText = "DTO_OrcaChatNoApiKey".Translate();
                SetError(statusText);
                return;
            }

            AddProcess("Tool budget exhausted; routing to dialogue model with existing tool results. Reason: " + reason + ".");
            transcript.AddMessage(LlmChatMessage.System(
                "Tool budget is exhausted. Use only the existing conversation and tool results already supplied. "
                + "Do not request or call more tools. The next assistant response must be exactly one JSON object and no extra text. "
                + "JSON schema: " + OrcaChatPromptBuilder.ChatReplyJsonSchema() + "."));
            ForceNextModelRole(OrcaLlmModelRole.Dialogue);
            StartRequest(settings);
        }
    }
}
