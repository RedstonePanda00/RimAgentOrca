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
                transcript.MarkChanged();
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
                RetryDialogueAfterUnexpectedToolRequest(response, line);
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
                transcript.RemoveDisplayLine(line);
                thinkingState.ForgetIfCurrent(line);
            }

            statusText = "DTO_OrcaChatWaiting".Translate();
            pendingStage = OrcaChatRequestStage.Chat;
            thinkingState.Ensure(transcript.DisplayLines, OrcaChatPromptBuilder.CurrentPersonaSpeakerName(), MarkConversationChanged);
            pendingRequest = client.SendPlainChatCompletionAsync(settings, transcript.SnapshotMessages(), pendingRequestRole);
            NotifyAgentPhase(OrcaChatRoleUtility.PhaseForRole(pendingRequestRole), pendingRequestRole, false, "streaming failed; fallback request sent");
            AddProcess("Streaming response failed; retrying once without streaming: " + error);
            AddProcess("Fallback request sent to " + OrcaChatRoleUtility.ModelRoleLabel(pendingRequestRole) + " model: " + settings.ModelForRole(pendingRequestRole));
            return true;
        }

        private void RetryDialogueAfterUnexpectedToolRequest(LlmChatResponse response, OrcaChatLine line)
        {
            if (line != null)
            {
                transcript.RemoveDisplayLine(line);
                thinkingState.ForgetIfCurrent(line);
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !settings.HasModelForRole(OrcaLlmModelRole.Dialogue))
            {
                statusText = "DTO_OrcaChatNoApiKey".Translate();
                SetError(statusText);
                return;
            }

            AddProcess("Dialogue model requested tool data; ignoring the tool request because controller owns routing decisions.");
            if (dialogueToolRequestRetryUsed)
            {
                SetError("Dialogue model requested tools again after retry; controller-owned routing prevented direct tool execution.");
                return;
            }

            dialogueToolRequestRetryUsed = true;
            transcript.AddMessage(LlmChatMessage.System(
                "The dialogue model attempted to request tools, but tool routing is controlled only by the controller model. "
                + "Ignore that tool request and produce the final player-facing reply using only the existing controller context, memory summary, conversation, and tool results already supplied. "
                + "Do not request tools. Requested tool hint was: " + OrcaToolCallFormatter.ToolCallHint(response)));
            ForceNextModelRole(OrcaLlmModelRole.Dialogue);
            StartRequest(settings);
        }

        private void StartRequest(DeepseekTheOrcaSettings settings)
        {
            transcript.Trim(MaxConversationTurns);
            transcript.RemoveOrphanToolMessages();
            OrcaLlmModelRole role = hasForcedNextModelRole ? forcedNextModelRole : OrcaChatRoleUtility.InitialChatModelRole(settings);
            ClearForcedNextModelRole();
            pendingRequestRole = role;
            pendingStage = OrcaChatRequestStage.Chat;
            currentModelRoleLabel = OrcaChatRoleUtility.ModelRoleLabel(role);
            currentModelReference = settings.ModelForRole(role);
            NotifyAgentPhase(OrcaChatRoleUtility.PhaseForRole(role), role, OrcaChatRoleUtility.ShouldStreamFinalReply(role), "request sent");
            OrcaChatLine thinkingLine = thinkingState.Ensure(transcript.DisplayLines, OrcaChatPromptBuilder.CurrentPersonaSpeakerName(), MarkConversationChanged);
            List<LlmChatMessage> requestMessages = SnapshotMessagesForRole(role);
            if (OrcaChatRoleUtility.ShouldStreamFinalReply(role))
            {
                pendingStreamingLine = thinkingLine;
                pendingStreamingRequest = client.StartStreamingPlainChatCompletion(
                    settings,
                    requestMessages,
                    900,
                    0.85f,
                    role);
                AddProcess("Streaming request sent to " + OrcaChatRoleUtility.ModelRoleLabel(role) + " model: " + settings.ModelForRole(role));
            }
            else
            {
                HashSet<string> allowedToolNames = role == OrcaLlmModelRole.Tool
                    ? AllowedToolNamesForToolRequest(includeExecutionPlanningTools: false)
                    : null;
                if (role == OrcaLlmModelRole.Tool)
                {
                    AddProcess("Tool bundle router selected " + (allowedToolNames == null ? 0 : allowedToolNames.Count) + " tool schema(s): " + (allowedToolNames == null ? "" : string.Join(", ", allowedToolNames.ToArray())));
                }
                pendingRequest = client.SendChatCompletionWithToolsAsync(
                    settings,
                    requestMessages,
                    LlmToolSchemas.BuildForRole(role, allowedToolNames),
                    900,
                    0.85f,
                    role);
                AddProcess("Request sent to " + OrcaChatRoleUtility.ModelRoleLabel(role) + " model: " + settings.ModelForRole(role));
            }
        }

        private HashSet<string> AllowedToolNamesForToolRequest(bool includeExecutionPlanningTools)
        {
            return AllowedToolNamesForToolRequest(includeExecutionPlanningTools, lastUserText);
        }

        private HashSet<string> AllowedToolNamesForToolRequest(bool includeExecutionPlanningTools, string queryText)
        {
            HashSet<string> allowedToolNames = OrcaToolBundleRouter.SelectToolNames(queryText, OrcaLlmModelRole.Tool, allowExecutionToolsThisTurn);
            foreach (string toolName in OrcaSkillManager.AllowedToolsForSkillIds(selectedSkillIdsThisTurn))
            {
                if (!toolName.NullOrEmpty())
                {
                    allowedToolNames.Add(toolName.Trim());
                }
            }

            if (includeExecutionPlanningTools && allowExecutionToolsThisTurn)
            {
                allowedToolNames.Add("list_available_incidents");
                allowedToolNames.Add("can_fire_incident");
                allowedToolNames.Add("propose_incident");
                allowedToolNames.Add("schedule_incident");
                allowedToolNames.Add("trigger_raid");
                allowedToolNames.Add("spawn_pawns");
            }

            if (!allowExecutionToolsThisTurn)
            {
                allowedToolNames.RemoveWhere(toolName => AiStoryToolRegistry.IsExecutionTool(toolName));
            }

            return allowedToolNames;
        }

        private List<LlmChatMessage> SnapshotMessagesForRole(OrcaLlmModelRole role)
        {
            List<LlmChatMessage> messages = role == OrcaLlmModelRole.Dialogue
                ? OrcaChatHistoryMaintenance.SnapshotForFinalDialogue(transcript.Messages)
                : transcript.SnapshotMessages();
            ApplySelectedSkillPromptForRole(messages, role);
            return messages;
        }

        private void ApplySelectedSkillPromptForRole(List<LlmChatMessage> messages, OrcaLlmModelRole role)
        {
            if (messages == null || selectedSkillIdsThisTurn == null || selectedSkillIdsThisTurn.Count == 0)
            {
                return;
            }

            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i] == null || messages[i].role != "user")
                {
                    continue;
                }

                List<string> contextTags = OrcaChatPromptBuilder.PlayerContextTags(lastUserText);
                OrcaChatTurnContext turnContext = new OrcaChatTurnContext(this, "player_chat", lastPlayerName, lastUserText, contextTags, false);
                messages[i].content = OrcaChatPromptBuilder.BuildPlayerMessage(turnContext, selectedSkillIdsThisTurn, role);
                return;
            }
        }

        private void ClearThinkingLine()
        {
            OrcaChatLine line = thinkingState.Consume();
            if (line == null)
            {
                return;
            }

            transcript.RemoveDisplayLine(line);
            if (pendingStreamingLine == line)
            {
                pendingStreamingLine = null;
            }
            MarkConversationChanged();
        }

    }
}
