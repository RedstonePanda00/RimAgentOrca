using System.Collections.Generic;

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
            selectedSkillIdsThisTurn.Clear();
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

        private void AddProcessLines(List<string> lines)
        {
            if (lines == null)
            {
                return;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                AddProcess(lines[i]);
            }
        }

        private void SetError(string error)
        {
            ClearThinkingLine();
            lastErrorText = error ?? "";
            if (currentTurn != null)
            {
                currentTurn.ErrorText = lastErrorText;
            }
        }
    }
}
