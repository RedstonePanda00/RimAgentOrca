using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;
namespace DeepseekTheOrca
{
    public sealed partial class OrcaChatSession
    {
        private void TickStreamingRequest()
        {
            if (pendingStreamingRequest == null)
            {
                return;
            }

            string before = pendingStreamingLine == null ? "" : pendingStreamingLine.Text ?? "";
            string visible = pendingStreamingRequest.VisibleText ?? "";
            string after = visible.NullOrEmpty() ? thinkingState.CurrentText() : visible;
            if (after != before)
            {
                if (pendingStreamingLine != null)
                {
                    pendingStreamingLine.Text = after;
                }
                conversationVersion++;
            }

            if (!pendingStreamingRequest.IsCompleted)
            {
                return;
            }

            LlmStreamingChatRequest completed = pendingStreamingRequest;
            OrcaChatLine line = pendingStreamingLine;
            pendingStreamingRequest = null;
            pendingStreamingLine = null;
            thinkingState.ForgetIfCurrent(line);

            LlmChatResponse response = completed.FinalResponse;
            if (response == null || !response.success)
            {
                string error = completed.ErrorMessage.NullOrEmpty() ? "Streaming response failed." : completed.ErrorMessage;
                if (TryStartNonStreamingDialogueFallback(line, error))
                {
                    return;
                }

                statusText = error;
                SetError(error);
                AddProcess("Streaming response failed; partial visible text was kept out of chat history and memory: " + error);
                return;
            }

            if (response.toolCalls.Count > 0)
            {
                RouteDialogueToolRequestToToolModel(response, line);
                return;
            }

            HandleFinalChatResponse(response, line);
        }

        private bool TryStartNonStreamingDialogueFallback(OrcaChatLine line, string error)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !settings.HasModelForRole(pendingRequestRole))
            {
                return false;
            }

            if (line != null)
            {
                displayLines.Remove(line);
                thinkingState.ForgetIfCurrent(line);
                conversationVersion++;
            }

            statusText = "DTO_OrcaChatWaiting".Translate();
            pendingStage = OrcaChatRequestStage.Chat;
            thinkingState.Ensure(displayLines, OrcaChatPromptBuilder.CurrentPersonaSpeakerName(), MarkConversationChanged);
            pendingRequest = client.SendPlainChatCompletionAsync(settings, new List<LlmChatMessage>(messages), pendingRequestRole);
            NotifyAgentPhase(OrcaChatRoleUtility.PhaseForRole(pendingRequestRole), pendingRequestRole, false, "streaming failed; fallback request sent");
            AddProcess("Streaming response failed; retrying once without streaming: " + error);
            AddProcess("Fallback request sent to " + OrcaChatRoleUtility.ModelRoleLabel(pendingRequestRole) + " model: " + settings.ModelForRole(pendingRequestRole));
            return true;
        }

        private void RouteDialogueToolRequestToToolModel(LlmChatResponse response, OrcaChatLine line)
        {
            if (line != null)
            {
                displayLines.Remove(line);
                thinkingState.ForgetIfCurrent(line);
                conversationVersion++;
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !settings.HasModelForRole(OrcaLlmModelRole.Tool) || toolRoundsUsed >= MaxToolGatheringRounds)
            {
                statusText = "DTO_OrcaChatToolBudgetReached".Translate();
                SetError(statusText);
                AddProcess("Dialogue model requested more tool data, but the tool model is unavailable or the tool budget is exhausted.");
                return;
            }

            AddProcess("Dialogue model requested more tool data; routing back to tool model.");
            NotifyAgentPhase(OrcaAgentPhase.NeedsMoreTools, OrcaLlmModelRole.Dialogue, false, "dialogue requested additional tool data");
            messages.Add(LlmChatMessage.System(
                "The dialogue model indicated that more game data is needed before the final player-facing reply. "
                + "Continue tool gathering now. Use tools if needed; do not write player-facing prose. "
                + "Requested tool hint: " + OrcaToolCallFormatter.ToolCallHint(response)));
            ForceNextModelRole(OrcaLlmModelRole.Tool);
            StartRequest(settings);
        }

        private void StartRequest(DeepseekTheOrcaSettings settings)
        {
            OrcaChatHistoryMaintenance.TrimConversation(messages, displayLines, MaxConversationTurns);
            OrcaChatHistoryMaintenance.RemoveOrphanToolMessages(messages);
            OrcaLlmModelRole role = hasForcedNextModelRole ? forcedNextModelRole : OrcaChatRoleUtility.InitialChatModelRole(settings);
            ClearForcedNextModelRole();
            pendingRequestRole = role;
            pendingStage = OrcaChatRequestStage.Chat;
            currentModelRoleLabel = OrcaChatRoleUtility.ModelRoleLabel(role);
            currentModelReference = settings.ModelForRole(role);
            NotifyAgentPhase(OrcaChatRoleUtility.PhaseForRole(role), role, OrcaChatRoleUtility.ShouldStreamFinalReply(role), "request sent");
            OrcaChatLine thinkingLine = thinkingState.Ensure(displayLines, OrcaChatPromptBuilder.CurrentPersonaSpeakerName(), MarkConversationChanged);
            if (OrcaChatRoleUtility.ShouldStreamFinalReply(role))
            {
                pendingStreamingLine = thinkingLine;
                pendingStreamingRequest = client.StartStreamingPlainChatCompletion(
                    settings,
                    new List<LlmChatMessage>(messages),
                    900,
                    0.85f,
                    role);
                AddProcess("Streaming request sent to " + OrcaChatRoleUtility.ModelRoleLabel(role) + " model: " + settings.ModelForRole(role));
            }
            else
            {
                pendingRequest = client.SendChatCompletionWithToolsAsync(
                    settings,
                    new List<LlmChatMessage>(messages),
                    LlmToolSchemas.BuildForRole(role),
                    900,
                    0.85f,
                    role);
                AddProcess("Request sent to " + OrcaChatRoleUtility.ModelRoleLabel(role) + " model: " + settings.ModelForRole(role));
            }
        }

        private void ClearThinkingLine()
        {
            OrcaChatLine line = thinkingState.Consume();
            if (line == null)
            {
                return;
            }

            displayLines.Remove(line);
            if (pendingStreamingLine == line)
            {
                pendingStreamingLine = null;
            }
            MarkConversationChanged();
        }

    }
}
