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
    public static class OrcaChatWindowManager
    {
        private static readonly OrcaChatSession session = new OrcaChatSession();

        public static OrcaChatSession Session
        {
            get { return session; }
        }

        public static bool IsOpen
        {
            get { return Find.WindowStack != null && Find.WindowStack.IsOpen(typeof(OrcaChatWindow)); }
        }

        public static void Toggle()
        {
            if (Find.WindowStack == null)
            {
                return;
            }

            if (IsOpen)
            {
                Find.WindowStack.TryRemove(typeof(OrcaChatWindow));
            }
            else
            {
                Find.WindowStack.Add(new OrcaChatWindow());
            }
        }

        public static void Open()
        {
            if (Find.WindowStack != null && !IsOpen)
            {
                Find.WindowStack.Add(new OrcaChatWindow());
            }
        }
    }

    public sealed class OrcaChatWindow : Window
    {
        private const string InputControlName = "DTO_OrcaChatInput";
        private Vector2 messageScrollPosition;
        private string inputBuffer = "";
        private int displayedConversationVersion = -1;

        public OrcaChatWindow()
        {
            doWindowBackground = false;
            doCloseX = false;
            closeOnAccept = false;
            closeOnCancel = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            draggable = true;
            resizeable = true;
            drawShadow = true;
            shadowAlpha = DeepseekTheOrcaMod.Settings == null ? 0.82f : Mathf.Clamp01(DeepseekTheOrcaMod.Settings.chatWindowAlpha);
            onlyOneOfTypeAllowed = true;
        }

        public override Vector2 InitialSize
        {
            get
            {
                OrcaChatWindowContext context = new OrcaChatWindowContext(OrcaChatWindowManager.Session, Rect.zero, Rect.zero, Rect.zero, 1f);
                return new Vector2(560f + OrcaExtensionManager.RequestedExtraWidth(context), 260f);
            }
        }

        protected override void SetInitialSizeAndPosition()
        {
            Vector2 initialSize = InitialSize;
            float x = Mathf.Clamp(80f, 0f, Mathf.Max(0f, UI.screenWidth - initialSize.x));
            float y = Mathf.Clamp(80f, 0f, Mathf.Max(0f, UI.screenHeight - initialSize.y));
            windowRect = new Rect(x, y, initialSize.x, initialSize.y).Rounded();
        }

        protected override float Margin
        {
            get { return 0f; }
        }

        public override void DoWindowContents(Rect inRect)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            float alpha = settings == null ? 0.82f : Mathf.Clamp01(settings.chatWindowAlpha);
            shadowAlpha = alpha;
            Widgets.DrawBoxSolid(inRect, new Color(0f, 0f, 0f, alpha));
            DrawFixedBorder(inRect);

            OrcaChatWindowContext measureContext = new OrcaChatWindowContext(OrcaChatWindowManager.Session, inRect, inRect, Rect.zero, alpha);
            float extensionWidth = Mathf.Min(
                OrcaExtensionManager.RequestedExtraWidth(measureContext),
                Mathf.Max(0f, inRect.width - 360f));
            float extensionGap = extensionWidth > 0f ? 8f : 0f;
            Rect chatRect = new Rect(inRect.x, inRect.y, inRect.width - extensionWidth - extensionGap, inRect.height);
            Rect extensionRect = extensionWidth > 0f
                ? new Rect(chatRect.xMax + extensionGap, inRect.y, extensionWidth, inRect.height)
                : Rect.zero;

            if (extensionWidth > 0f)
            {
                Widgets.DrawBoxSolid(new Rect(chatRect.xMax + extensionGap * 0.5f, inRect.y + 8f, 1f, inRect.height - 16f), new Color(0.55f, 0.55f, 0.55f, 0.75f));
            }

            Rect contentRect = chatRect.ContractedBy(10f);
            float y = 0f;

            Text.Font = GameFont.Small;
            Rect messagesRect = new Rect(contentRect.x, contentRect.y + y, contentRect.width, contentRect.height - y - 96f);
            DrawMessages(messagesRect);
            y += messagesRect.height + 8f;

            Rect inputRect = new Rect(contentRect.x, contentRect.y + y, contentRect.width, 64f);
            HandleEnterToSend();
            GUI.SetNextControlName(InputControlName);
            inputBuffer = Widgets.TextArea(inputRect, inputBuffer);
            y += 70f;

            OrcaChatWindowContext drawContext = new OrcaChatWindowContext(OrcaChatWindowManager.Session, inRect, chatRect, extensionRect, alpha);
            if (extensionWidth > 0f)
            {
                OrcaExtensionManager.DrawRightExtensions(extensionRect.ContractedBy(8f), drawContext);
            }
            OrcaExtensionManager.DrawOverlays(inRect, drawContext);
            DrawCloseButton(inRect);

            OrcaChatWindowManager.Session.Tick();
        }

        private void DrawCloseButton(Rect rect)
        {
            Rect buttonRect = new Rect(rect.xMax - 20f, rect.y + 6f, 18f, 18f);
            Rect imageRect = new Rect(buttonRect.x + 4.5f, buttonRect.y + 4.5f, 9f, 9f);
            GUI.DrawTexture(imageRect, TexButton.CloseXSmall);
            if (Widgets.ButtonInvisible(buttonRect))
            {
                Close();
            }
        }

        private static void DrawFixedBorder(Rect rect)
        {
            Color borderColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 1f), borderColor);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), borderColor);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 1f, rect.height), borderColor);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), borderColor);
        }

        private void DrawMessages(Rect outRect)
        {
            string text = OrcaChatWindowManager.Session.DisplayText;
            if (text.NullOrEmpty())
            {
                return;
            }

            Rect innerRect = new Rect(outRect.x + 6f, outRect.y + 6f, outRect.width - 12f, outRect.height - 12f);
            float viewWidth = Mathf.Max(10f, innerRect.width - 16f);
            float viewHeight = Mathf.Max(innerRect.height, Text.CalcHeight(text, viewWidth) + 8f);

            if (displayedConversationVersion != OrcaChatWindowManager.Session.ConversationVersion)
            {
                displayedConversationVersion = OrcaChatWindowManager.Session.ConversationVersion;
                messageScrollPosition.y = Mathf.Max(0f, viewHeight - innerRect.height);
            }

            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
            Widgets.BeginScrollView(innerRect, ref messageScrollPosition, viewRect);
            Widgets.Label(new Rect(0f, 0f, viewWidth, viewHeight), text);
            Widgets.EndScrollView();
        }

        private void HandleEnterToSend()
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.KeyDown)
            {
                return;
            }

            if (GUI.GetNameOfFocusedControl() != InputControlName)
            {
                return;
            }

            if (current.shift)
            {
                return;
            }

            if (current.keyCode != KeyCode.Return && current.keyCode != KeyCode.KeypadEnter)
            {
                return;
            }

            if (!OrcaChatWindowManager.Session.IsWaiting && !inputBuffer.NullOrEmpty())
            {
                SendInputBuffer();
            }

            current.Use();
        }

        private void SendInputBuffer()
        {
            string text = inputBuffer == null ? "" : inputBuffer.TrimEnd('\r', '\n');
            if (text.NullOrEmpty())
            {
                inputBuffer = "";
                return;
            }

            OrcaChatWindowManager.Session.Send(text);
            inputBuffer = "";
            displayedConversationVersion = -1;
            GUI.FocusControl(null);
        }
    }

    public sealed class OrcaChatSession
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
            messages.Add(LlmChatMessage.User(BuildPlayerMessage(playerName, userText)));
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
            messages.Add(LlmChatMessage.User(BuildProactiveMessage(request)));
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

            if (pendingRequestRole == OrcaLlmModelRole.Tool && toolRoundsUsed > 0)
            {
                DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
                if (settings == null || !settings.HasModelForRole(OrcaLlmModelRole.Dialogue))
                {
                    statusText = "DTO_OrcaChatNoApiKey".Translate();
                    SetError(statusText);
                    return;
                }

                AddProcess("Tool model produced no further tool calls; routing to dialogue model.");
                messages.Add(LlmChatMessage.System(
                    "Tool gathering is complete. The next assistant response must be exactly one JSON object and no extra text. "
                    + "JSON schema: " + ChatReplyJsonSchema() + "."));
                ForceNextModelRole(OrcaLlmModelRole.Dialogue);
                StartRequest(settings);
                return;
            }

            HandleFinalChatResponse(response, null);
        }

        private void HandleFinalChatResponse(LlmChatResponse response, OrcaChatLine existingLine)
        {
            string content = response.content ?? "";
            OrcaChatReply parsed = OrcaChatReply.Parse(content);
            string originalReply = parsed.reply ?? "";
            parsed.reply = SanitizeVisibleReply(originalReply);
            if (parsed.reply != originalReply)
            {
                AddProcess("Visible reply control markup removed from model output.");
            }
            if (OrcaMoodPlugin.Enabled)
            {
                lastMoodDelta = parsed.moodDelta;
                mood = Mathf.Clamp(mood + parsed.moodDelta, 0, 100);
            }
            else
            {
                lastMoodDelta = 0;
            }
            if (!parsed.parsedJson)
            {
                AddProcess("Final response was plain text; normalized it for chat history and kept mood delta at 0.");
            }
            AddProcess("Final response received.");
            if (OrcaMoodPlugin.Enabled)
            {
                AddProcess("Mood delta: " + (lastMoodDelta >= 0 ? "+" + lastMoodDelta : lastMoodDelta.ToString()) + "; mood now: " + mood);
            }

            messages.Add(LlmChatMessage.Assistant(parsed.HistoryContent(content), null));
            if (existingLine != null)
            {
                existingLine.Text = parsed.reply;
            }
            else
            {
                displayLines.Add(new OrcaChatLine("DTO_OrcaChatSpeakerOrca".Translate(), parsed.reply));
            }
            OrcaSessionMemory.Add("orca_reply", OrcaMoodPlugin.Enabled ? parsed.reply + " moodDelta=" + parsed.moodDelta + " moodNow=" + mood : parsed.reply);
            lastReplyText = parsed.reply;
            if (currentTurn != null)
            {
                currentTurn.ReplyText = parsed.reply;
            }
            conversationVersion++;
            NotifyAgentPhase(OrcaAgentPhase.Completed, pendingRequestRole, false, "final reply received");
            TrimConversation();
            statusText = "DTO_OrcaChatReady".Translate();
        }

        private void TickStreamingRequest()
        {
            if (pendingStreamingRequest == null)
            {
                return;
            }

            string before = pendingStreamingLine == null ? "" : pendingStreamingLine.Text ?? "";
            string visible = pendingStreamingRequest.VisibleText ?? "";
            string after = visible.NullOrEmpty() ? ThinkingText() : visible;
            if (after != before)
            {
                if (pendingStreamingLine != null)
                {
                    pendingStreamingLine.Text = after;
                }
                conversationVersion++;
            }

            if (!pendingStreamingRequest.IsCompleted)
            {
                return;
            }

            LlmStreamingChatRequest completed = pendingStreamingRequest;
            OrcaChatLine line = pendingStreamingLine;
            pendingStreamingRequest = null;
            pendingStreamingLine = null;

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
                RouteDialogueToolRequestToToolModel(response, line);
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
                displayLines.Remove(line);
                conversationVersion++;
            }

            statusText = "DTO_OrcaChatWaiting".Translate();
            pendingStage = OrcaChatRequestStage.Chat;
            pendingRequest = client.SendPlainChatCompletionAsync(settings, new List<LlmChatMessage>(messages), pendingRequestRole);
            NotifyAgentPhase(PhaseForRole(pendingRequestRole), pendingRequestRole, false, "streaming failed; fallback request sent");
            AddProcess("Streaming response failed; retrying once without streaming: " + error);
            AddProcess("Fallback request sent to " + ModelRoleLabel(pendingRequestRole) + " model: " + settings.ModelForRole(pendingRequestRole));
            return true;
        }

        private void RouteDialogueToolRequestToToolModel(LlmChatResponse response, OrcaChatLine line)
        {
            if (line != null)
            {
                displayLines.Remove(line);
                conversationVersion++;
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !settings.HasModelForRole(OrcaLlmModelRole.Tool) || toolRoundsUsed >= MaxToolRounds)
            {
                statusText = "DTO_OrcaChatToolBudgetReached".Translate();
                SetError(statusText);
                AddProcess("Dialogue model requested more tool data, but the tool model is unavailable or the tool budget is exhausted.");
                return;
            }

            AddProcess("Dialogue model requested more tool data; routing back to tool model.");
            NotifyAgentPhase(OrcaAgentPhase.NeedsMoreTools, OrcaLlmModelRole.Dialogue, false, "dialogue requested additional tool data");
            messages.Add(LlmChatMessage.System(
                "The dialogue model indicated that more game data is needed before the final player-facing reply. "
                + "Continue tool gathering now. Use tools if needed; do not write player-facing prose. "
                + "Requested tool hint: " + ToolCallHint(response)));
            ForceNextModelRole(OrcaLlmModelRole.Tool);
            StartRequest(settings);
        }

        public void Clear()
        {
            messages.Clear();
            displayLines.Clear();
            statusText = "";
            pendingRequest = null;
            pendingStreamingRequest = null;
            pendingStreamingLine = null;
            pendingStage = OrcaChatRequestStage.Chat;
            pendingRequestRole = OrcaLlmModelRole.Fallback;
            mood = 60;
            lastMoodDelta = 0;
            toolRoundsUsed = 0;
            ClearForcedNextModelRole();
            lastUserText = "";
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

        private void StartRequest(DeepseekTheOrcaSettings settings)
        {
            TrimConversation();
            RemoveOrphanToolMessages();
            OrcaLlmModelRole role = hasForcedNextModelRole ? forcedNextModelRole : InitialChatModelRole(settings);
            ClearForcedNextModelRole();
            pendingRequestRole = role;
            pendingStage = OrcaChatRequestStage.Chat;
            currentModelRoleLabel = ModelRoleLabel(role);
            currentModelReference = settings.ModelForRole(role);
            NotifyAgentPhase(PhaseForRole(role), role, ShouldStreamFinalReply(role), "request sent");
            if (ShouldStreamFinalReply(role))
            {
                pendingStreamingLine = new OrcaChatLine("DTO_OrcaChatSpeakerOrca".Translate(), ThinkingText());
                displayLines.Add(pendingStreamingLine);
                conversationVersion++;
                pendingStreamingRequest = client.StartStreamingPlainChatCompletion(
                    settings,
                    new List<LlmChatMessage>(messages),
                    900,
                    0.85f,
                    role);
                AddProcess("Streaming request sent to " + ModelRoleLabel(role) + " model: " + settings.ModelForRole(role));
            }
            else
            {
                pendingRequest = client.SendChatCompletionWithToolsAsync(
                    settings,
                    new List<LlmChatMessage>(messages),
                    LlmToolSchemas.BuildChatTools(),
                    900,
                    0.85f,
                    role);
                AddProcess("Request sent to " + ModelRoleLabel(role) + " model: " + settings.ModelForRole(role));
            }
        }

        private static bool ShouldStreamFinalReply(OrcaLlmModelRole role)
        {
            return role == OrcaLlmModelRole.Dialogue;
        }

        private static OrcaAgentPhase PhaseForRole(OrcaLlmModelRole role)
        {
            switch (role)
            {
                case OrcaLlmModelRole.Controller:
                    return OrcaAgentPhase.Routing;
                case OrcaLlmModelRole.Tool:
                case OrcaLlmModelRole.WebSearch:
                case OrcaLlmModelRole.Vision:
                    return OrcaAgentPhase.ToolGathering;
                case OrcaLlmModelRole.Dialogue:
                    return OrcaAgentPhase.FinalReply;
                default:
                    return OrcaAgentPhase.Unknown;
            }
        }

        private void NotifyAgentPhase(OrcaAgentPhase phase, OrcaLlmModelRole role, bool streaming, string reason)
        {
            OrcaExtensionManager.NotifyAgentPhase(new OrcaAgentPhaseContext(this, phase, role, toolRoundsUsed, streaming, reason));
        }

        private static string ThinkingText()
        {
            int frame = (Find.TickManager == null ? 0 : Find.TickManager.TicksGame / ThinkingAnimationIntervalTicks) % 3;
            return "Thinking" + new string('.', frame + 1);
        }

        private void StartControllerOrChatRequest(DeepseekTheOrcaSettings settings)
        {
            if (settings != null && settings.HasModelForRole(OrcaLlmModelRole.Controller))
            {
                StartControllerRequest(settings);
                return;
            }

            lastControllerRoute = "direct";
            StartRequest(settings);
        }

        private void StartControllerRequest(DeepseekTheOrcaSettings settings)
        {
            pendingStage = OrcaChatRequestStage.Controller;
            pendingRequest = client.SendPlainChatCompletionAsync(settings, BuildControllerMessages(), OrcaLlmModelRole.Controller);
            currentModelRoleLabel = ModelRoleLabel(OrcaLlmModelRole.Controller);
            currentModelReference = settings.ModelForRole(OrcaLlmModelRole.Controller);
            AddProcess("Request sent to controller model: " + settings.ModelForRole(OrcaLlmModelRole.Controller));
            NotifyAgentPhase(OrcaAgentPhase.Routing, OrcaLlmModelRole.Controller, false, "controller request sent");
        }

        private List<LlmChatMessage> BuildControllerMessages()
        {
            string latestUserContent = "";
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].role == "user")
                {
                    latestUserContent = messages[i].content ?? "";
                    break;
                }
            }

            List<LlmChatMessage> controllerMessages = new List<LlmChatMessage>();
            string skillRoutingHint = OrcaSkillManager.FormatControllerRoutingHint();
            string pluginRoutingHint = DeepseekTheOrcaMod.FormatPluginControllerRoutingHint();
            controllerMessages.Add(LlmChatMessage.System(
                "You are the chat controller model. Route the latest RimWorld chat turn to exactly one specialist. "
                + "Return exactly one JSON object and no extra text. "
                + "Schema: {\"route\":\"dialogue|tool|web_search|vision\",\"reason\":\"short reason\"}. "
                + "Use dialogue for ordinary conversation and final wording. "
                + "Use tool when current game state, pawns, incidents, RimTalk history, MCP tools, or event execution may be needed. "
                + "Use web_search only for current external public-web information outside the game. "
                + "Use vision only when the request clearly depends on image recognition. "
                + "If unsure, choose tool when game state might matter, otherwise dialogue. "
                + skillRoutingHint
                + pluginRoutingHint));
            controllerMessages.Add(LlmChatMessage.User(latestUserContent));
            return controllerMessages;
        }

        private void HandleControllerResponse(LlmChatResponse response)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !HasAnyChatModel(settings))
            {
                statusText = "DTO_OrcaChatNoApiKey".Translate();
                SetError(statusText);
                return;
            }

            string route = ParseControllerRoute(response.content);
            OrcaLlmModelRole role = ModelRoleForControllerRoute(route, settings);
            OrcaAgentRoutingContext routingContext = new OrcaAgentRoutingContext(this, route, role, "controller route");
            OrcaExtensionManager.ModifyAgentRouting(routingContext);
            route = routingContext.route;
            role = routingContext.requestedRole;
            lastControllerRoute = route;
            ForceNextModelRole(role);
            AddProcess("Controller route: " + route + " -> " + ModelRoleLabel(role) + " model.");
            if (routingContext.Changed)
            {
                AddProcess("Extension adjusted route to " + route + " -> " + ModelRoleLabel(role) + " model.");
            }
            StartRequest(settings);
        }

        private static string ParseControllerRoute(string content)
        {
            try
            {
                Dictionary<string, object> parsed = MiniJson.Deserialize(ExtractJsonObject(content ?? "")) as Dictionary<string, object>;
                string route = GetParsedString(parsed, "route");
                if (!route.NullOrEmpty())
                {
                    return route.Trim().ToLowerInvariant();
                }
            }
            catch
            {
            }

            return "dialogue";
        }

        private static OrcaLlmModelRole ModelRoleForControllerRoute(string route, DeepseekTheOrcaSettings settings)
        {
            if (route == "web_search")
            {
                return OrcaLlmModelRole.WebSearch;
            }

            if (route == "vision")
            {
                return OrcaLlmModelRole.Vision;
            }

            if (route == "tool")
            {
                return OrcaLlmModelRole.Tool;
            }

            return OrcaLlmModelRole.Dialogue;
        }

        private void HandleToolCalls(LlmChatResponse response)
        {
            if (toolRoundsUsed >= MaxToolRounds)
            {
                statusText = "DTO_OrcaChatToolBudgetReached".Translate();
                return;
            }

            toolRoundsUsed++;
            statusText = "DTO_OrcaChatUsingTools".Translate();
            AddProcess("Received " + response.toolCalls.Count + " tool call(s), round " + toolRoundsUsed + ".");
            messages.Add(LlmChatMessage.Assistant(response.content, response.toolCalls));

            AiToolContext context = new AiToolContext(Find.CurrentMap, null, null);
            AiToolSession session = new AiToolSession(context);
            for (int i = 0; i < response.toolCalls.Count; i++)
            {
                LlmToolCall toolCall = response.toolCalls[i];
                AiToolResult result;
                Dictionary<string, string> arguments = ParseArguments(toolCall.argumentsJson);
                AddProcess("Tool call: " + toolCall.name + " " + FormatArguments(arguments));
                if (!IsToolExposedToChat(toolCall.name))
                {
                    result = AiToolResult.Fail("tool is not exposed to chat: " + toolCall.name);
                }
                else if (toolCall.name == "web_search" && (DeepseekTheOrcaMod.Settings == null || !DeepseekTheOrcaMod.Settings.UsesLocalWebSearchTool))
                {
                    result = AiToolResult.Fail("web search is disabled in mod settings");
                }
                else if (Find.CurrentMap == null && ToolRequiresCurrentMap(toolCall.name))
                {
                    result = AiToolResult.Fail("no current map");
                }
                else if (!allowExecutionToolsThisTurn && !ToolAllowsDuringProactive(toolCall.name))
                {
                    result = AiToolResult.Fail("tool is disabled for proactive trigger turns");
                }
                else if (toolCall.name == "schedule_incident")
                {
                    result = InvokeScheduleIncidentFromChat(session, arguments);
                }
                else if (toolCall.name == "trigger_raid")
                {
                    result = InvokeTriggerRaidFromChat(session, arguments);
                }
                else if (toolCall.name == "spawn_pawns")
                {
                    result = InvokeSpawnPawnsFromChat(session, arguments);
                }
                else
                {
                    result = session.Invoke(toolCall.name, arguments);
                }

                AddProcess("Tool result: " + (result.success ? "ok" : "failed") + " - " + result.message + FormatValues(result));
                totalToolCalls++;
                lastToolName = toolCall.name;
                lastToolResult = (result.success ? "ok" : "failed") + " - " + result.message;
                if (!result.success)
                {
                    failedToolCalls++;
                }
                RecordToolMemory(toolCall.name, result);
                messages.Add(LlmChatMessage.Tool(toolCall.id, SerializeToolResult(result)));
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            OrcaLlmModelRole nextRole = toolRoundsUsed >= MaxToolRounds || settings == null || !settings.HasModelForRole(OrcaLlmModelRole.Tool)
                ? OrcaLlmModelRole.Dialogue
                : OrcaLlmModelRole.Tool;
            if (settings == null || !settings.HasModelForRole(nextRole))
            {
                statusText = "DTO_OrcaChatNoApiKey".Translate();
                SetError(statusText);
                return;
            }

            if (nextRole == OrcaLlmModelRole.Tool)
            {
                messages.Add(LlmChatMessage.System(
                    "Tool results have been supplied. If more game data is needed to satisfy the player's request, call another tool. "
                    + "If enough information has been gathered, do not call tools; the dialogue model will write the final player-facing response."));
            }
            else
            {
                messages.Add(LlmChatMessage.System(
                    "Tool results have been supplied. The next assistant response must be exactly one JSON object and no extra text. "
                    + "JSON schema: " + ChatReplyJsonSchema() + "."));
            }

            ForceNextModelRole(nextRole);
            StartRequest(settings);
        }

        private static bool HasAnyChatModel(DeepseekTheOrcaSettings settings)
        {
            return settings != null
                && (settings.HasModelForRole(OrcaLlmModelRole.Dialogue)
                    || settings.HasModelForRole(OrcaLlmModelRole.Tool)
                    || settings.HasModelForRole(OrcaLlmModelRole.WebSearch)
                    || settings.HasModelForRole(OrcaLlmModelRole.Vision));
        }

        private void ForceNextModelRole(OrcaLlmModelRole role)
        {
            forcedNextModelRole = role;
            hasForcedNextModelRole = true;
        }

        private void ClearForcedNextModelRole()
        {
            forcedNextModelRole = OrcaLlmModelRole.Fallback;
            hasForcedNextModelRole = false;
        }

        private static OrcaLlmModelRole InitialChatModelRole(DeepseekTheOrcaSettings settings)
        {
            if (settings != null && !settings.toolModel.NullOrEmpty())
            {
                return OrcaLlmModelRole.Tool;
            }

            return FirstAvailableChatModelRole(settings);
        }

        private static OrcaLlmModelRole FirstAvailableChatModelRole(DeepseekTheOrcaSettings settings)
        {
            if (settings != null && !settings.toolModel.NullOrEmpty() && settings.HasModelForRole(OrcaLlmModelRole.Tool))
            {
                return OrcaLlmModelRole.Tool;
            }

            if (settings != null && settings.HasModelForRole(OrcaLlmModelRole.Dialogue))
            {
                return OrcaLlmModelRole.Dialogue;
            }

            if (settings != null && !settings.webSearchModel.NullOrEmpty() && settings.HasModelForRole(OrcaLlmModelRole.WebSearch))
            {
                return OrcaLlmModelRole.WebSearch;
            }

            if (settings != null && !settings.visionModel.NullOrEmpty() && settings.HasModelForRole(OrcaLlmModelRole.Vision))
            {
                return OrcaLlmModelRole.Vision;
            }

            return OrcaLlmModelRole.Fallback;
        }

        private static string ExtractJsonObject(string content)
        {
            int start = content.IndexOf('{');
            int end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return content.Substring(start, end - start + 1);
            }

            return content;
        }

        private static string GetParsedString(Dictionary<string, object> parsed, string key)
        {
            object value;
            if (parsed == null || !parsed.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            return value.ToString();
        }

        private static string ModelRoleLabel(OrcaLlmModelRole role)
        {
            switch (role)
            {
                case OrcaLlmModelRole.Decision:
                    return "decision";
                case OrcaLlmModelRole.Controller:
                    return "controller";
                case OrcaLlmModelRole.Dialogue:
                    return "dialogue";
                case OrcaLlmModelRole.Tool:
                    return "tool";
                case OrcaLlmModelRole.Vision:
                    return "vision";
                case OrcaLlmModelRole.WebSearch:
                    return "web-search";
                default:
                    return "fallback";
            }
        }

        private string BuildPlayerMessage(string playerName, string userText)
        {
            StringBuilder builder = new StringBuilder();
            if (OrcaMoodPlugin.Enabled)
            {
                builder.AppendLine("System mood value: " + mood);
            }
            builder.AppendLine("Player SteamPersonaName: " + playerName);
            AppendMemoryContext(builder);
            AppendActiveSkillContext(builder, userText);
            builder.AppendLine("Player message:");
            builder.Append(userText);
            return builder.ToString();
        }

        private string BuildProactiveMessage(OrcaProactiveConversationRequest request)
        {
            StringBuilder builder = new StringBuilder();
            if (OrcaMoodPlugin.Enabled)
            {
                builder.AppendLine("System mood value: " + mood);
            }
            builder.AppendLine("Current game language: " + OrcaLanguageUtility.CurrentGameLanguage());
            builder.AppendLine("System proactive trigger source: " + request.source);
            builder.AppendLine("Trigger title: " + request.title);
            AppendMemoryContext(builder);
            AppendActiveSkillContext(builder, request.source + "\n" + request.title + "\n" + request.body);
            builder.AppendLine("Trigger details:");
            builder.AppendLine(request.body);
            builder.Append("This is not a player request. Speak proactively to the player in character. Reply in the current game language, even if the trigger details use English field labels. Do not call event execution tools for this trigger; the event has already been scheduled or observed.");
            return builder.ToString();
        }

        private static void AppendActiveSkillContext(StringBuilder builder, string turnText)
        {
            string skillPrompt = OrcaSkillManager.FormatActiveSkillPrompt(turnText);
            if (skillPrompt.NullOrEmpty())
            {
                return;
            }

            builder.AppendLine("Skill harness:");
            builder.AppendLine(skillPrompt);
        }

        private static void AppendMemoryContext(StringBuilder builder)
        {
            string memoryContext = OrcaSessionMemory.ContextForPrompt();
            if (memoryContext.NullOrEmpty())
            {
                return;
            }

            builder.AppendLine("Memory context:");
            builder.AppendLine(memoryContext);
        }

        private static void RecordToolMemory(string toolName, AiToolResult result)
        {
            if (result == null)
            {
                return;
            }

            if (!IsToolExposedToChat(toolName))
            {
                return;
            }

            OrcaSessionMemory.Add("tool_" + toolName, (result.success ? "ok" : "failed") + " - " + result.message + FormatValues(result));
        }

        private void EnsureSystemPrompt()
        {
            if (messages.Count > 0)
            {
                return;
            }

            messages.Add(LlmChatMessage.System(BuildSystemPrompt()));
        }

        private static string BuildSystemPrompt()
        {
            StringBuilder builder = new StringBuilder();
            string personaPrompt = CurrentPersonaPrompt();
            if (!personaPrompt.NullOrEmpty())
            {
                builder.AppendLine(personaPrompt.Trim());
                builder.AppendLine();
            }

            string pluginPrompt = DeepseekTheOrcaMod.FormatEnabledPluginPrompt();
            if (!pluginPrompt.NullOrEmpty())
            {
                builder.AppendLine(pluginPrompt);
                builder.AppendLine();
            }

            builder.AppendLine("Common chat runtime rules:");
            builder.AppendLine("A memory context may be included in user messages. It is process-level memory for the current RimWorld launch, shared across saves, and reset when the game process restarts. Treat it as soft memory: useful for continuity, but less authoritative than current game data from tools.");
            builder.AppendLine("Never mention hidden rolls, willingness chance, percentages, dice rolls, random rolls, validation, tool calls, JSON, internal state, or tool result internals to the player.");
            builder.AppendLine("You may inspect game data through tools when it would help you answer naturally: colony summary, recent letters, map pawns, pawn details, available incidents, and RimTalk chat history if available.");
            builder.AppendLine("If web search is available, you may use it for current external information outside the game. Do not use web search for current RimWorld colony state; use game tools for that. Treat web results as imperfect and summarize them naturally.");
            builder.AppendLine("If external MCP tools are available, they were configured by the player. Use them only when they directly help with the player's request, and treat their results as external tool output rather than RimWorld game state.");
            builder.AppendLine("RimTalk history may be read without explicit permission when it helps you understand colony conversation, player behavior, pawn relationships, or a proactive trigger. Its playerName is the value of RimTalk's player address/name configuration; do not treat it as the player's real name or SteamPersonaName. It only indicates how RimTalk was configured to refer to the player in that mod's dialogue context. Origin distinguishes player_initiated from ai_auto_generated dialogue.");
            builder.AppendLine("If a user message says it is a system proactive trigger, it is not from the player. Speak proactively about that trigger. For RimTalk proactive triggers, you may read RimTalk history before replying. Do not call execution tools for proactive triggers because the event was already scheduled or observed.");
            builder.AppendLine("The reply field is player-visible natural language only. Do not include XML-like tags, HTML-like tags, hidden channels, or control markup in reply.");
            builder.AppendLine("Respond in the same language the player uses unless asked otherwise. For proactive triggers, use the current game/player language rather than English trigger labels.");
            builder.AppendLine("Output exactly one JSON object and no extra text. JSON schema: " + ChatReplyJsonSchema() + ".");
            return builder.ToString();
        }

        private static string ChatReplyJsonSchema()
        {
            return OrcaMoodPlugin.Enabled ? "{\"reply\":\"visible reply text\",\"moodDelta\":0}" : "{\"reply\":\"visible reply text\"}";
        }

        private static string CurrentPersonaPrompt()
        {
            string defName = DeepseekTheOrcaMod.Settings == null ? OrcaChatPersonaManager.BuiltInOrcaId : DeepseekTheOrcaMod.Settings.chatPersonaDefName;
            OrcaChatPersonaProfile persona = OrcaChatPersonaManager.Get(defName);
            if (persona == null)
            {
                persona = OrcaChatPersonaManager.Get(OrcaChatPersonaManager.BuiltInOrcaId);
            }

            return persona == null ? "" : persona.prompt;
        }

        private AiToolResult InvokeScheduleIncidentFromChat(AiToolSession session, Dictionary<string, string> arguments)
        {
            AiToolResult validationResult = session.Invoke("schedule_incident", arguments);
            if (!validationResult.success)
            {
                return validationResult;
            }

            float chance = IncidentWillingnessChance(arguments);
            AddProcess("Willingness roll chance: " + chance.ToStringPercent());
            if (!Rand.Chance(chance))
            {
                AddProcess("Willingness roll failed.");
                return AiToolResult.Fail("Orca was unwilling to actually fire the incident.");
            }
            AddProcess("Willingness roll passed.");

            StorytellerComp_DeepseekOrca comp = ActiveOrcaComp();
            if (comp == null)
            {
                return AiToolResult.Fail("active storyteller does not contain StorytellerComp_DeepseekOrca");
            }

            AiIncidentPlan plan;
            string rejectReason;
            if (!TryBuildPlan(arguments, out plan, out rejectReason))
            {
                return AiToolResult.Fail(rejectReason);
            }

            string message;
            string traceText;
            bool fired = comp.TryFireIncidentNowForDebug(Find.CurrentMap, plan, out message, out traceText);
            if (!fired)
            {
                return AiToolResult.Fail(message);
            }

            return AiToolResult.Ok(message)
                .WithValue("incidentDef", plan.incidentDefName)
                .WithValue("reason", plan.reason ?? "");
        }

        private static string SanitizeVisibleReply(string text)
        {
            if (text.NullOrEmpty())
            {
                return text ?? "";
            }

            return OrcaVisibleReplySanitizer.Sanitize(text, trim: true);
        }

        private AiToolResult InvokeTriggerRaidFromChat(AiToolSession session, Dictionary<string, string> arguments)
        {
            AiToolResult validationResult = session.Invoke("trigger_raid", arguments);
            if (!validationResult.success)
            {
                return validationResult;
            }

            float chance = AggressiveWillingnessChance();
            AddProcess("Willingness roll chance: " + chance.ToStringPercent());
            if (!Rand.Chance(chance))
            {
                AddProcess("Willingness roll failed.");
                return AiToolResult.Fail("Orca was unwilling to actually fire the raid.");
            }
            AddProcess("Willingness roll passed.");

            StorytellerComp_DeepseekOrca comp = ActiveOrcaComp();
            if (comp == null)
            {
                return AiToolResult.Fail("active storyteller does not contain StorytellerComp_DeepseekOrca");
            }

            string message;
            string traceText;
            bool fired = comp.TryFireRaidNowForDebug(Find.CurrentMap, arguments, out message, out traceText);
            if (!traceText.NullOrEmpty())
            {
                AddProcess("Trigger raid trace: " + traceText.Replace("\n", " | "));
            }
            if (!fired)
            {
                return AiToolResult.Fail(message);
            }

            return AiToolResult.Ok(message);
        }

        private AiToolResult InvokeSpawnPawnsFromChat(AiToolSession session, Dictionary<string, string> arguments)
        {
            AiToolResult validationResult = session.Invoke("spawn_pawns", arguments);
            if (!validationResult.success)
            {
                return validationResult;
            }

            float chance = SpawnPawnsWillingnessChance(arguments);
            AddProcess("Willingness roll chance: " + chance.ToStringPercent());
            if (!Rand.Chance(chance))
            {
                AddProcess("Willingness roll failed.");
                return AiToolResult.Fail("Orca was unwilling to actually spawn pawns.");
            }
            AddProcess("Willingness roll passed.");

            AiToolContext context = new AiToolContext(Find.CurrentMap, null, null);
            string message;
            bool spawned = OrcaPawnSpawnUtility.TrySpawnPawns(context, arguments, out message);
            if (!spawned)
            {
                return AiToolResult.Fail(message);
            }

            return AiToolResult.Ok(message);
        }

        private static bool IsExecutionTool(string toolName)
        {
            return AiStoryToolRegistry.IsExecutionTool(toolName);
        }

        private static bool IsToolExposedToChat(string toolName)
        {
            if (toolName == "web_search" && (DeepseekTheOrcaMod.Settings == null || !DeepseekTheOrcaMod.Settings.UsesLocalWebSearchTool))
            {
                return false;
            }

            return AiStoryToolRegistry.IsExposedToChat(toolName) || OrcaHttpMcpClient.IsExposedTool(toolName);
        }

        private static bool ToolAllowsDuringProactive(string toolName)
        {
            return OrcaHttpMcpClient.IsExposedTool(toolName) || AiStoryToolRegistry.AllowsDuringProactive(toolName);
        }

        private static bool ToolRequiresCurrentMap(string toolName)
        {
            return !OrcaHttpMcpClient.IsExposedTool(toolName) && AiStoryToolRegistry.RequiresCurrentMap(toolName);
        }

        private float HelpfulWillingnessChance()
        {
            if (!OrcaMoodPlugin.Enabled)
            {
                return 1f;
            }

            return Mathf.Clamp(mood, 0, 100) / 100f;
        }

        private float AggressiveWillingnessChance()
        {
            float helpful = HelpfulWillingnessChance();
            if (mood <= 9)
            {
                return Mathf.Max(helpful, 1f - helpful);
            }

            return helpful;
        }

        private float IncidentWillingnessChance(Dictionary<string, string> arguments)
        {
            string incidentDef = GetArgument(arguments, "incidentDef");
            return IsPunitiveIncidentDef(incidentDef) ? AggressiveWillingnessChance() : HelpfulWillingnessChance();
        }

        private float SpawnPawnsWillingnessChance(Dictionary<string, string> arguments)
        {
            return IsHostileFactionArgument(arguments) ? AggressiveWillingnessChance() : HelpfulWillingnessChance();
        }

        private static bool IsPunitiveIncidentDef(string incidentDef)
        {
            if (incidentDef.NullOrEmpty())
            {
                return false;
            }

            string text = incidentDef.ToLowerInvariant();
            return text.Contains("raid")
                || text.Contains("manhunter")
                || text.Contains("infestation")
                || text.Contains("mech")
                || text.Contains("shipchunk")
                || text.Contains("shippart")
                || text.Contains("defoliator")
                || text.Contains("psychic")
                || text.Contains("toxic")
                || text.Contains("plague")
                || text.Contains("disease")
                || text.Contains("mad")
                || text.Contains("insanity")
                || text.Contains("volcanic")
                || text.Contains("cold")
                || text.Contains("heat")
                || text.Contains("eclipse");
        }

        private static bool IsHostileFactionArgument(Dictionary<string, string> arguments)
        {
            Faction faction = FindFaction(GetArgument(arguments, "factionDef"));
            return faction != null && Faction.OfPlayer != null && faction.HostileTo(Faction.OfPlayer);
        }

        private static Faction FindFaction(string factionText)
        {
            if (Find.FactionManager == null || factionText.NullOrEmpty())
            {
                return null;
            }

            string needle = factionText.Trim();
            FactionDef factionDef = DefDatabase<FactionDef>.GetNamedSilentFail(needle);
            if (factionDef != null)
            {
                Faction byDef = Find.FactionManager.FirstFactionOfDef(factionDef);
                if (byDef != null)
                {
                    return byDef;
                }
            }

            return Find.FactionManager.AllFactionsListForReading.FirstOrDefault(faction =>
                faction != null
                && (string.Equals(faction.def.defName, needle, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(faction.Name, needle, StringComparison.OrdinalIgnoreCase)
                    || (!faction.Name.NullOrEmpty() && faction.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)));
        }

        private static string GetArgument(Dictionary<string, string> arguments, string key)
        {
            string value;
            return arguments != null && arguments.TryGetValue(key, out value) ? value : "";
        }

        private static StorytellerComp_DeepseekOrca ActiveOrcaComp()
        {
            if (Find.Storyteller == null || Find.Storyteller.storytellerComps == null)
            {
                return null;
            }

            for (int i = 0; i < Find.Storyteller.storytellerComps.Count; i++)
            {
                StorytellerComp_DeepseekOrca comp = Find.Storyteller.storytellerComps[i] as StorytellerComp_DeepseekOrca;
                if (comp != null)
                {
                    return comp;
                }
            }

            return null;
        }

        private static bool TryBuildPlan(Dictionary<string, string> arguments, out AiIncidentPlan plan, out string rejectReason)
        {
            plan = null;
            rejectReason = null;

            string incidentDef;
            if (arguments == null || !arguments.TryGetValue("incidentDef", out incidentDef) || incidentDef.NullOrEmpty())
            {
                rejectReason = "missing argument: incidentDef";
                return false;
            }

            float pointsFactor = 1f;
            string pointsFactorText;
            if (arguments.TryGetValue("pointsFactor", out pointsFactorText))
            {
                float.TryParse(pointsFactorText, out pointsFactor);
            }

            string reason;
            arguments.TryGetValue("reason", out reason);
            plan = AiIncidentPlan.For(incidentDef, reason ?? "Orca chat selected this incident.", pointsFactor);
            return true;
        }

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

    public sealed class OrcaChatLine
    {
        public readonly string Speaker;
        public string Text;

        public OrcaChatLine(string speaker, string text)
        {
            Speaker = speaker;
            Text = text;
        }
    }

    public sealed class OrcaChatTurnLog
    {
        public readonly int Sequence;
        public readonly string UserText;
        public string ProcessText = "";
        public string ReplyText = "";
        public string ErrorText = "";

        public OrcaChatTurnLog(int sequence, string userText)
        {
            Sequence = sequence;
            UserText = userText ?? "";
        }

        public string Label
        {
            get
            {
                string text = StripRichTextTags(UserText).Replace("\n", " ").Replace("\r", " ");
                if (text.Length > 24)
                {
                    text = text.Substring(0, 24) + "...";
                }

                return "#" + Sequence + " " + text;
            }
        }

        private static string StripRichTextTags(string text)
        {
            if (text.NullOrEmpty())
            {
                return "";
            }

            StringBuilder builder = new StringBuilder(text.Length);
            bool inTag = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '<')
                {
                    inTag = true;
                    continue;
                }

                if (ch == '>' && inTag)
                {
                    inTag = false;
                    continue;
                }

                if (!inTag)
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }
    }

    public sealed class OrcaChatReply
    {
        public string reply;
        public int moodDelta;
        public bool parsedJson;

        public string HistoryContent(string originalContent)
        {
            if (parsedJson && !OrcaVisibleReplySanitizer.ContainsControlMarkup(originalContent))
            {
                return originalContent ?? "";
            }

            Dictionary<string, object> normalized = new Dictionary<string, object>();
            normalized["reply"] = reply ?? "";
            normalized["moodDelta"] = moodDelta;
            return MiniJson.Serialize(normalized);
        }

        public static OrcaChatReply Parse(string content)
        {
            if (content.NullOrEmpty())
            {
                return new OrcaChatReply { reply = "", moodDelta = 0, parsedJson = false };
            }

            try
            {
                Dictionary<string, object> parsed = MiniJson.Deserialize(ExtractJsonObject(content)) as Dictionary<string, object>;
                if (parsed == null)
                {
                    return new OrcaChatReply { reply = content, moodDelta = 0, parsedJson = false };
                }

                string reply = GetString(parsed, "reply");
                int moodDelta = ClampMoodDelta(GetInt(parsed, "moodDelta"));
                return new OrcaChatReply
                {
                    reply = reply.NullOrEmpty() ? content : reply,
                    moodDelta = moodDelta,
                    parsedJson = true
                };
            }
            catch
            {
                return new OrcaChatReply { reply = content, moodDelta = 0, parsedJson = false };
            }
        }

        private static string ExtractJsonObject(string content)
        {
            int start = content.IndexOf('{');
            int end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return content.Substring(start, end - start + 1);
            }

            return content;
        }

        private static string GetString(Dictionary<string, object> parsed, string key)
        {
            object value;
            if (!parsed.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            return value.ToString();
        }

        private static int GetInt(Dictionary<string, object> parsed, string key)
        {
            object value;
            if (!parsed.TryGetValue(key, out value) || value == null)
            {
                return 0;
            }

            int intValue;
            if (int.TryParse(value.ToString(), out intValue))
            {
                return intValue;
            }

            float floatValue;
            if (float.TryParse(value.ToString(), out floatValue))
            {
                return Mathf.RoundToInt(floatValue);
            }

            return 0;
        }

        private static int ClampMoodDelta(int value)
        {
            if (value < -10)
            {
                return -10;
            }

            if (value > 10)
            {
                return 10;
            }

            return value;
        }
    }
}
