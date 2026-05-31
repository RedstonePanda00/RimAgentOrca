namespace DeepseekTheOrca
{
    public sealed partial class OrcaChatSession
    {
        public void Clear()
        {
            messages.Clear();
            displayLines.Clear();
            statusText = "";
            pendingRequest = null;
            pendingStreamingRequest = null;
            pendingStreamingLine = null;
            thinkingState.Clear();
            pendingStage = OrcaChatRequestStage.Chat;
            pendingRequestRole = OrcaLlmModelRole.Fallback;
            OrcaExtensionManager.NotifyChatSessionCleared(this);
            toolRoundsUsed = 0;
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
            conversationVersion++;
        }
    }
}
