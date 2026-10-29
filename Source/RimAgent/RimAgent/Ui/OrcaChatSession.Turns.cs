using System;
using System.Collections.Generic;
using Verse;

namespace RimAgent;

public sealed partial class OrcaChatSession
{
    private bool StartTurn(ChatTurn turn)
    {
        if (IsWaiting || string.IsNullOrWhiteSpace(turn.Text)) return false;
        ChatTurnExecutor executor = OrcaExternalBridge.IsControlled ? new ExternalChatExecutor() : new NativeChatExecutor();
        var settings = RimAgentMod.Settings;
        if (!executor.Available(settings))
        {
            if (!turn.Proactive) { BeginTurnLog(turn.LogText); statusText = "DTO_OrcaChatNoApiKey".Translate(); SetError(statusText); }
            return false;
        }
        try
        {
            executor.Validate(turn);
            BeginTurnLog(turn.LogText);
            EnsureSystemPrompt();
            var context = turn.Context(this);
            OrcaExtensionManager.NotifyChatTurnStarting(context);
            AddProcessLines(context.ProcessLines);
            var message = turn.BuildMessage(context);
            transcript.AddMessage(LlmChatMessage.User(message));
            turn.RecordInput(this);
            transcript.MarkChanged();
            transcript.Trim(MaxConversationTurns);
            toolRoundsUsed = 0;
            toolCallsUsedThisTurn = 0;
            allowExecutionToolsThisTurn = !turn.Proactive;
            specialistReturnedNoToolCalls = false;
            dialogueToolRequestRetryUsed = false;
            ResetParallelToolState();
            finalReplyReceivedThisTurn = false;
            turnCompletionNotified = false;
            ClearForcedNextModelRole();
            executor.Start(this, settings, turn, context, message);
            return true;
        }
        catch (Exception ex) { SetError(ex.Message); return false; }
    }

    private abstract class ChatTurn(string text)
    {
        public string Text { get; } = text;
        public abstract bool Proactive { get; }
        public virtual string LogText => Text;
        public abstract OrcaChatTurnContext Context(OrcaChatSession session);
        public abstract string BuildMessage(OrcaChatTurnContext context);
        public abstract void RecordInput(OrcaChatSession session);
    }
    private sealed class PlayerChatTurn(string text) : ChatTurn(text)
    {
        private readonly string player = OrcaPlayerIdentity.SteamPersonaName();
        public override bool Proactive => false;
        public override OrcaChatTurnContext Context(OrcaChatSession session)
            => new(session, "player_chat", player, Text, OrcaChatPromptBuilder.PlayerContextTags(Text), false);
        public override string BuildMessage(OrcaChatTurnContext context) => OrcaChatPromptBuilder.BuildPlayerMessage(context, null);
        public override void RecordInput(OrcaChatSession session)
        {
            session.lastPlayerName = player;
            OrcaSessionMemory.Add("player_message", player + ": " + Text);
            session.transcript.AddDisplayLine(new OrcaChatLine(player, Text));
        }
    }
    private sealed class ProactiveChatTurn(OrcaProactiveConversationRequest request) : ChatTurn(request.title + "\n" + request.body)
    {
        public override bool Proactive => true;
        public override string LogText => "Proactive: " + Text;
        public override OrcaChatTurnContext Context(OrcaChatSession session)
            => new(session, request.source, "", Text, OrcaChatPromptBuilder.ProactiveContextTags(request), true);
        public override string BuildMessage(OrcaChatTurnContext context) => OrcaChatPromptBuilder.BuildProactiveMessage(request, context);
        public override void RecordInput(OrcaChatSession session)
        {
            OrcaSessionMemory.Add("proactive_trigger", request.source + " | " + request.title + " | " + request.body);
            if (request.openChatWindow) OrcaChatWindowManager.Open();
        }
    }

    private abstract class ChatTurnExecutor
    {
        public abstract bool Available(RimAgentSettings settings);
        public virtual void Validate(ChatTurn turn) { }
        public abstract void Start(OrcaChatSession session, RimAgentSettings settings, ChatTurn turn, OrcaChatTurnContext context, string message);
    }
    private sealed class NativeChatExecutor : ChatTurnExecutor
    {
        public override bool Available(RimAgentSettings settings) => settings != null && OrcaChatRoleUtility.HasAnyChatModel(settings);
        public override void Start(OrcaChatSession session, RimAgentSettings settings, ChatTurn turn, OrcaChatTurnContext context, string message)
        {
            session.statusText = "DTO_OrcaChatWaiting".Translate();
            if (turn.Proactive)
            {
                session.ForceNextModelRole(OrcaLlmModelRole.Dialogue);
                session.PrepareSemanticQuery(settings, () => session.StartRequest(settings));
            }
            else session.PrepareSemanticQuery(settings, () => session.StartControllerOrChatRequest(settings));
        }
    }
    private sealed class ExternalChatExecutor : ChatTurnExecutor
    {
        public override bool Available(RimAgentSettings settings) => true;
        public override void Validate(ChatTurn turn)
        {
            if (turn.Text.Length > 8000) throw new InvalidOperationException("External chat message exceeds 8000 characters.");
        }
        public override void Start(OrcaChatSession session, RimAgentSettings settings, ChatTurn turn, OrcaChatTurnContext context, string message)
            => session.StartExternalRequest(turn, context, message);
    }
}
