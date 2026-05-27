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
        private const int MaxConversationTurns = 12;
        private const int MaxTurnLogs = 50;
        private const int MaxToolRounds = 8;
        private const int ThinkingAnimationIntervalTicks = 30;

        private enum OrcaChatRequestStage
        {
            Chat,
            Controller
        }

        private readonly LlmApiClient client = new LlmApiClient();
        private readonly List<LlmChatMessage> messages = new List<LlmChatMessage>();
        private readonly List<OrcaChatLine> displayLines = new List<OrcaChatLine>();
        private Task<LlmChatResponse> pendingRequest;
        private LlmStreamingChatRequest pendingStreamingRequest;
        private OrcaChatLine pendingStreamingLine;
        private string statusText = "";
        private int mood = 60;
        private int lastMoodDelta;
        private int conversationVersion;
        private int toolRoundsUsed;
        private readonly List<OrcaChatTurnLog> turnLogs = new List<OrcaChatTurnLog>();
        private OrcaChatTurnLog currentTurn;
        private string lastUserText = "";
        private string lastPlayerName = "Player";
        private string lastProcessText = "";
        private string lastReplyText = "";
        private string lastErrorText = "";
        private readonly List<string> processLines = new List<string>();
        private bool allowExecutionToolsThisTurn = true;
        private OrcaChatRequestStage pendingStage;
        private bool hasForcedNextModelRole;
        private OrcaLlmModelRole forcedNextModelRole;
        private OrcaLlmModelRole pendingRequestRole;
        private string lastControllerRoute = "direct";
        private string currentModelRoleLabel = "";
        private string currentModelReference = "";
        private int totalToolCalls;
        private int failedToolCalls;
        private string lastToolName = "";
        private string lastToolResult = "";

        public bool IsWaiting
        {
            get { return pendingRequest != null || pendingStreamingRequest != null; }
        }

        public string StatusText
        {
            get { return statusText; }
        }

        public int Mood
        {
            get { return mood; }
        }

        public int LastMoodDelta
        {
            get { return lastMoodDelta; }
        }

        public int ConversationVersion
        {
            get { return conversationVersion; }
        }

        public float WillingnessChance
        {
            get { return OrcaMoodPlugin.Enabled ? Mathf.Clamp(mood, 0, 100) / 100f : 1f; }
        }

        public string DisplayText
        {
            get
            {
                return string.Join("\n\n", displayLines.Select(line => line.Speaker + ": " + line.Text).ToArray());
            }
        }

        public string LastUserText
        {
            get { return lastUserText; }
        }

        public string LastProcessText
        {
            get { return lastProcessText; }
        }

        public string LastReplyText
        {
            get { return lastReplyText; }
        }

        public string LastErrorText
        {
            get { return lastErrorText; }
        }

        public List<OrcaChatTurnLog> TurnLogs
        {
            get { return turnLogs; }
        }

        public string LastControllerRoute
        {
            get { return lastControllerRoute; }
        }

        public string CurrentModelRoleLabel
        {
            get { return currentModelRoleLabel; }
        }

        public string CurrentModelReference
        {
            get { return currentModelReference; }
        }

        public int TotalToolCalls
        {
            get { return totalToolCalls; }
        }

        public int FailedToolCalls
        {
            get { return failedToolCalls; }
        }

        public string LastToolName
        {
            get { return lastToolName; }
        }

        public string LastToolResult
        {
            get { return lastToolResult; }
        }

        public void Send(string userText)
        {
            if (IsWaiting)
            {
                return;
            }

            userText = userText == null ? "" : userText.TrimEnd('\r', '\n');
            if (userText.NullOrEmpty())
            {
                return;
            }

            BeginTurnLog(userText);
            if (OrcaMoodPlugin.Enabled)
            {
                AddProcess("Mood before request: " + mood);
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !HasAnyChatModel(settings))
            {
                statusText = "DTO_OrcaChatNoApiKey".Translate();
                SetError(statusText);
                return;
            }

            EnsureSystemPrompt();
            string playerName = PlayerSteamPersonaName();
            lastPlayerName = playerName;
            messages.Add(LlmChatMessage.User(BuildPlayerMessage(playerName, userText, PlayerContextTags(userText), null)));
            OrcaSessionMemory.Add("player_message", playerName + ": " + userText);
            displayLines.Add(new OrcaChatLine(playerName, userText));
            conversationVersion++;
            TrimConversation();

            statusText = "DTO_OrcaChatWaiting".Translate();
            toolRoundsUsed = 0;
            allowExecutionToolsThisTurn = true;
            ClearForcedNextModelRole();
            StartControllerOrChatRequest(settings);
        }

        public bool TryStartProactive(OrcaProactiveConversationRequest request)
        {
            if (IsWaiting || request == null)
            {
                return false;
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !HasAnyChatModel(settings))
            {
                return false;
            }

            BeginTurnLog("Proactive: " + request.title + "\n" + request.body);
            if (OrcaMoodPlugin.Enabled)
            {
                AddProcess("Mood before proactive request: " + mood);
            }
            EnsureSystemPrompt();
            messages.Add(LlmChatMessage.User(BuildProactiveMessage(request, ProactiveContextTags(request))));
            OrcaSessionMemory.Add("proactive_trigger", request.source + " | " + request.title + " | " + request.body);
            conversationVersion++;
            TrimConversation();

            if (request.openChatWindow)
            {
                OrcaChatWindowManager.Open();
            }

            statusText = "DTO_OrcaChatWaiting".Translate();
            toolRoundsUsed = 0;
            allowExecutionToolsThisTurn = false;
            ClearForcedNextModelRole();
            ForceNextModelRole(OrcaLlmModelRole.Dialogue);
            StartRequest(settings);
            return true;
        }

        public void Tick()
        {
            TickStreamingRequest();

            if (pendingRequest == null || !pendingRequest.IsCompleted)
            {
                return;
            }

            LlmChatResponse response;
            try
            {
                response = pendingRequest.Result;
            }
            catch (Exception ex)
            {
                statusText = ex.GetType().Name + ": " + ex.Message;
                SetError(statusText);
                pendingRequest = null;
                return;
            }

            pendingRequest = null;
            if (!response.success)
            {
                statusText = response.errorMessage;
                SetError(response.errorMessage);
                return;
            }

            if (pendingStage == OrcaChatRequestStage.Controller)
            {
                HandleControllerResponse(response);
                return;
            }

            if (response.toolCalls.Count > 0)
            {
                HandleToolCalls(response);
                return;
            }

            if (IsToolGatheringRole(pendingRequestRole) && toolRoundsUsed > 0)
            {
                DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
                if (settings == null || !settings.HasModelForRole(OrcaLlmModelRole.Dialogue))
                {
                    statusText = "DTO_OrcaChatNoApiKey".Translate();
                    SetError(statusText);
                    return;
                }

                AddProcess(ModelRoleLabel(pendingRequestRole) + " model produced no further tool calls; routing to dialogue model.");
                messages.Add(LlmChatMessage.System(
                    "Tool gathering is complete. The next assistant response must be exactly one JSON object and no extra text. "
                    + "JSON schema: " + ChatReplyJsonSchema() + "."));
                ForceNextModelRole(OrcaLlmModelRole.Dialogue);
                StartRequest(settings);
                return;
            }

            HandleFinalChatResponse(response, null);
        }
    }
}
