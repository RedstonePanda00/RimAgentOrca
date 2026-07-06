using Verse;

namespace DeepseekTheOrca
{
    public sealed partial class OrcaChatSession
    {
        private void HandleFinalChatResponse(LlmChatResponse response, OrcaChatLine existingLine)
        {
            string content = response.content ?? "";
            OrcaChatReply parsed = OrcaChatReply.Parse(content);
            string originalReply = parsed.reply ?? "";
            parsed.reply = OrcaVisibleReplySanitizer.Sanitize(originalReply, trim: true);
            if (parsed.reply != originalReply)
            {
                AddProcess("Visible reply control markup removed from model output.");
            }

            OrcaChatReplyContext replyContext = OrcaExtensionManager.NotifyChatReply(this, parsed, content);
            AddProcessLines(replyContext.ProcessLines);
            if (!parsed.parsedJson)
            {
                AddProcess("Final response was plain text; normalized it for chat history and extension fields.");
            }
            AddProcess("Final response received.");

            transcript.AddMessage(LlmChatMessage.Assistant(parsed.HistoryContent(), null));
            if (existingLine != null)
            {
                existingLine.Text = parsed.reply;
            }
            else
            {
                transcript.AddDisplayLine(new OrcaChatLine(OrcaChatPromptBuilder.CurrentPersonaSpeakerName(), parsed.reply));
            }

            string memoryText = parsed.reply;
            if (replyContext.MemoryFragments.Count > 0)
            {
                memoryText += " " + string.Join(" ", replyContext.MemoryFragments.ToArray());
            }

            OrcaSessionMemory.Add("agent_reply", memoryText);
            lastReplyText = parsed.reply;
            if (currentTurn != null)
            {
                currentTurn.ReplyText = parsed.reply;
            }

            transcript.MarkChanged();
            finalReplyReceivedThisTurn = true;
            CompleteTurnIfIdle("final reply received");
        }

        private void CompleteTurnIfIdle(string reason)
        {
            if (turnCompletionNotified || !finalReplyReceivedThisTurn)
            {
                return;
            }

            if (pendingRequest != null || pendingStreamingRequest != null || pendingParallelToolRequest != null)
            {
                statusText = pendingParallelToolRequest != null ? "DTO_OrcaChatUsingTools".Translate() : "DTO_OrcaChatWaiting".Translate();
                return;
            }

            turnCompletionNotified = true;
            NotifyAgentPhase(OrcaAgentPhase.Completed, pendingRequestRole, false, reason);
            transcript.Trim(MaxConversationTurns);
            statusText = "DTO_OrcaChatReady".Translate();
        }
    }
}
