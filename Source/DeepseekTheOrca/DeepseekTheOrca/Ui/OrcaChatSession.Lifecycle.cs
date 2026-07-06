namespace DeepseekTheOrca
{
    public sealed partial class OrcaChatSession
    {
        public void Clear()
        {
            transcript.Clear();
            statusText = "";
            pendingRequest = null;
            pendingStreamingRequest = null;
            pendingStreamingLine = null;
            ResetParallelToolState();
            thinkingState.Clear();
            pendingStage = OrcaChatRequestStage.Chat;
            pendingRequestRole = OrcaLlmModelRole.Fallback;
            OrcaExtensionManager.NotifyChatSessionCleared(this);
            toolRoundsUsed = 0;
            toolCallsUsedThisTurn = 0;
            ClearForcedNextModelRole();
            lastUserText = "";
            lastPlayerName = "Player";
            lastProcessText = "";
            lastReplyText = "";
            lastErrorText = "";
            processLines.Clear();
            turnLogs.Clear();
            currentTurn = null;
            lastControllerRoute = "direct";
            currentModelRoleLabel = "";
            currentModelReference = "";
            totalToolCalls = 0;
            failedToolCalls = 0;
            lastToolName = "";
            lastToolResult = "";
            specialistReturnedNoToolCalls = false;
            dialogueToolRequestRetryUsed = false;
            finalReplyReceivedThisTurn = false;
            turnCompletionNotified = false;
            selectedSkillIdsThisTurn.Clear();
        }
    }
}
