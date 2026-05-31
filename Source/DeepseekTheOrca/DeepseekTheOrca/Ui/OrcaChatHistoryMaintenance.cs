using System.Collections.Generic;

namespace DeepseekTheOrca
{
    public static class OrcaChatHistoryMaintenance
    {
        public static void TrimConversation(List<LlmChatMessage> messages, List<OrcaChatLine> displayLines, int maxConversationTurns)
        {
            if (messages != null)
            {
                while (ConversationTurnCount(messages) > maxConversationTurns)
                {
                    int removeEnd = NextUserMessageIndex(messages, 2);
                    if (removeEnd < 0)
                    {
                        break;
                    }

                    messages.RemoveRange(1, removeEnd - 1);
                }
            }

            if (displayLines != null)
            {
                while (displayLines.Count > maxConversationTurns * 2)
                {
                    displayLines.RemoveAt(0);
                }
            }
        }

        public static void RemoveOrphanToolMessages(List<LlmChatMessage> messages)
        {
            if (messages == null)
            {
                return;
            }

            bool awaitingToolResponse = false;
            for (int i = 0; i < messages.Count; i++)
            {
                LlmChatMessage message = messages[i];
                if (message.role == "assistant")
                {
                    awaitingToolResponse = message.toolCalls != null && message.toolCalls.Count > 0;
                    continue;
                }

                if (message.role == "tool")
                {
                    if (!awaitingToolResponse)
                    {
                        messages.RemoveAt(i);
                        i--;
                    }
                    continue;
                }

                awaitingToolResponse = false;
            }
        }

        private static int ConversationTurnCount(List<LlmChatMessage> messages)
        {
            int count = 0;
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i].role == "user")
                {
                    count++;
                }
            }

            return count;
        }

        private static int NextUserMessageIndex(List<LlmChatMessage> messages, int startIndex)
        {
            for (int i = startIndex; i < messages.Count; i++)
            {
                if (messages[i].role == "user")
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
