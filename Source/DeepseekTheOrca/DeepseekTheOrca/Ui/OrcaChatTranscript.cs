using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepseekTheOrca
{
    // Owns the chat history (LLM messages + display lines) and its maintenance,
    // including trimming and orphan-tool-message cleanup. The version counter
    // lets the UI detect transcript changes cheaply.
    public sealed class OrcaChatTranscript
    {
        private readonly List<LlmChatMessage> messages = new List<LlmChatMessage>();
        private readonly List<OrcaChatLine> displayLines = new List<OrcaChatLine>();
        private int version;

        public List<LlmChatMessage> Messages
        {
            get { return messages; }
        }

        public List<OrcaChatLine> DisplayLines
        {
            get { return displayLines; }
        }

        public int Version
        {
            get { return version; }
        }

        public string DisplayText
        {
            get
            {
                return string.Join("\n\n", displayLines.Select(line => line.Speaker + ": " + line.Text).ToArray());
            }
        }

        public void MarkChanged()
        {
            version++;
        }

        public void AddMessage(LlmChatMessage message)
        {
            messages.Add(message);
        }

        public void AddDisplayLine(OrcaChatLine line)
        {
            displayLines.Add(line);
        }

        public void RemoveDisplayLine(OrcaChatLine line)
        {
            if (line != null && displayLines.Remove(line))
            {
                version++;
            }
        }

        public List<LlmChatMessage> SnapshotMessages()
        {
            return new List<LlmChatMessage>(messages);
        }

        public void EnsureSystemPrompt(Func<string> promptBuilder)
        {
            if (messages.Count == 0)
            {
                messages.Add(LlmChatMessage.System(promptBuilder()));
            }
        }

        public int LatestUserMessageIndex()
        {
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].role == "user")
                {
                    return i;
                }
            }

            return -1;
        }

        public void Trim(int maxTurns)
        {
            OrcaChatHistoryMaintenance.TrimConversation(messages, displayLines, maxTurns);
        }

        public void RemoveOrphanToolMessages()
        {
            OrcaChatHistoryMaintenance.RemoveOrphanToolMessages(messages);
        }

        public void Clear()
        {
            messages.Clear();
            displayLines.Clear();
            version++;
        }
    }
}
