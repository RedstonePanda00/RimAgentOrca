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
        private void BeginTurnLog(string userText)
        {
            lastUserText = userText;
            lastReplyText = "";
            lastErrorText = "";
            processLines.Clear();
            lastProcessText = "";
            currentTurn = new OrcaChatTurnLog(turnLogs.Count + 1, userText);
            turnLogs.Add(currentTurn);
            while (turnLogs.Count > MaxTurnLogs)
            {
                turnLogs.RemoveAt(0);
            }
        }

        private void AddProcess(string line)
        {
            processLines.Add(line);
            lastProcessText = string.Join("\n", processLines.ToArray());
            if (currentTurn != null)
            {
                currentTurn.ProcessText = lastProcessText;
            }
        }

        private void SetError(string error)
        {
            lastErrorText = error ?? "";
            if (currentTurn != null)
            {
                currentTurn.ErrorText = lastErrorText;
            }
        }

        private static Dictionary<string, string> ParseArguments(string argumentsJson)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (argumentsJson.NullOrEmpty())
            {
                return result;
            }

            result["__rawJson"] = argumentsJson;
            try
            {
                Dictionary<string, object> parsed = MiniJson.Deserialize(argumentsJson) as Dictionary<string, object>;
                if (parsed == null)
                {
                    return result;
                }

                foreach (KeyValuePair<string, object> pair in parsed)
                {
                    result[pair.Key] = pair.Value == null ? "" : pair.Value.ToString();
                }
            }
            catch (Exception ex)
            {
                result["parseError"] = ex.Message;
            }

            return result;
        }

        private static string FormatArguments(Dictionary<string, string> arguments)
        {
            if (arguments == null || arguments.Count == 0)
            {
                return "{}";
            }

            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string> pair in arguments)
            {
                if (pair.Key == "__rawJson")
                {
                    continue;
                }

                parts.Add(pair.Key + "=" + pair.Value);
            }

            return "{" + string.Join(", ", parts.ToArray()) + "}";
        }

        private static string ToolCallHint(LlmChatResponse response)
        {
            if (response == null || response.toolCalls == null || response.toolCalls.Count == 0)
            {
                return "none";
            }

            List<string> parts = new List<string>();
            for (int i = 0; i < response.toolCalls.Count; i++)
            {
                LlmToolCall toolCall = response.toolCalls[i];
                if (toolCall == null)
                {
                    continue;
                }

                parts.Add((toolCall.name ?? "") + " " + (toolCall.argumentsJson ?? "{}"));
            }

            return parts.Count == 0 ? "none" : string.Join(" | ", parts.ToArray());
        }

        private static string FormatValues(AiToolResult result)
        {
            if (result.values == null || result.values.Count == 0)
            {
                return "";
            }

            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string> pair in result.values)
            {
                parts.Add(pair.Key + "=" + pair.Value);
            }

            return " [" + string.Join(", ", parts.ToArray()) + "]";
        }

        private static string SerializeToolResult(AiToolResult result)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["success"] = result.success;
            payload["message"] = result.message ?? "";
            payload["values"] = result.values;
            return MiniJson.Serialize(payload);
        }

        private void TrimConversation()
        {
            while (ConversationTurnCount() > MaxConversationTurns)
            {
                int removeEnd = NextUserMessageIndex(2);
                if (removeEnd < 0)
                {
                    break;
                }

                messages.RemoveRange(1, removeEnd - 1);
            }

            while (displayLines.Count > MaxConversationTurns * 2)
            {
                displayLines.RemoveAt(0);
            }
        }

        private int ConversationTurnCount()
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

        private int NextUserMessageIndex(int startIndex)
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

        private void RemoveOrphanToolMessages()
        {
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

        private static string PlayerSteamPersonaName()
        {
            string personaName = SteamUtility.SteamPersonaName;
            return personaName.NullOrEmpty() || personaName == "???" ? "Player" : personaName;
        }
    }
}
