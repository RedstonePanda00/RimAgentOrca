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

        public static List<LlmChatMessage> SnapshotForFinalDialogue(List<LlmChatMessage> messages)
        {
            List<LlmChatMessage> result = new List<LlmChatMessage>();
            if (messages == null)
            {
                return result;
            }

            for (int i = 0; i < messages.Count; i++)
            {
                LlmChatMessage message = messages[i];
                if (message == null)
                {
                    continue;
                }

                if (message.role == "assistant" && message.toolCalls != null && message.toolCalls.Count > 0)
                {
                    List<LlmChatMessage> toolMessages = new List<LlmChatMessage>();
                    int j = i + 1;
                    while (j < messages.Count && messages[j] != null && messages[j].role == "tool")
                    {
                        toolMessages.Add(messages[j]);
                        j++;
                    }

                    result.Add(LlmChatMessage.System(FormatToolResultSummary(message, toolMessages)));
                    i = j - 1;
                    continue;
                }

                if (message.role == "system" && IsInternalRoutingControlMessage(message.content))
                {
                    continue;
                }

                if (message.role == "tool")
                {
                    result.Add(LlmChatMessage.System("Tool result already gathered for final dialogue:\n" + (message.content ?? "")));
                    continue;
                }

                result.Add(CloneMessage(message, stripToolCalls: true));
            }

            return result;
        }

        private static string FormatToolResultSummary(LlmChatMessage assistantMessage, List<LlmChatMessage> toolMessages)
        {
            List<string> lines = new List<string>();
            lines.Add("Tool results already gathered for final dialogue. Use these results as evidence. Do not request or call more tools.");
            List<LlmToolCall> calls = assistantMessage.toolCalls ?? new List<LlmToolCall>();
            int count = toolMessages == null ? 0 : toolMessages.Count;
            for (int i = 0; i < count; i++)
            {
                string toolName = i < calls.Count && calls[i] != null ? calls[i].name : "tool";
                string content = toolMessages[i] == null ? "" : toolMessages[i].content ?? "";
                lines.Add("- " + toolName + ": " + content);
            }

            if (count == 0)
            {
                lines.Add("- Tool call was requested, but no tool result is available.");
            }

            return string.Join("\n", lines.ToArray());
        }

        private static bool IsInternalRoutingControlMessage(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return false;
            }

            return content.StartsWith("Specialist tool results have been supplied.")
                || content.StartsWith("Controller review requested more specialist data from the ");
        }

        private static LlmChatMessage CloneMessage(LlmChatMessage message, bool stripToolCalls)
        {
            if (message == null)
            {
                return null;
            }

            return new LlmChatMessage
            {
                role = message.role,
                content = message.content,
                toolCallId = stripToolCalls ? "" : message.toolCallId,
                toolCalls = stripToolCalls ? null : CloneToolCalls(message.toolCalls)
            };
        }

        private static List<LlmToolCall> CloneToolCalls(List<LlmToolCall> calls)
        {
            if (calls == null || calls.Count == 0)
            {
                return null;
            }

            List<LlmToolCall> result = new List<LlmToolCall>();
            for (int i = 0; i < calls.Count; i++)
            {
                LlmToolCall call = calls[i];
                if (call == null)
                {
                    continue;
                }

                result.Add(new LlmToolCall
                {
                    id = call.id,
                    name = call.name,
                    argumentsJson = call.argumentsJson
                });
            }

            return result;
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
