using System;
using System.Linq;
using System.Text.Json.Nodes;
using RimAgent.Core;
using Verse;

namespace RimAgent;

public sealed partial class OrcaChatSession : IOrcaExternalChat
{
    private RelayRequest externalRequest;
    public void PrepareExternalControl()
    {
        externalRequest?.Cancel(); externalRequest = null;
        visualCancellation?.Cancel();
        client.CancelQueuedRequests();
        afterSemanticQuery = null; pendingSemanticQuery = null;
        pendingStreamingRequest?.Cancel(); pendingStreamingRequest = null; pendingRequest = null;
        pendingToolBatch?.Dispose(); pendingToolBatch = null;
        pendingParallelBatch?.Dispose(); pendingParallelBatch = null;
        ResetParallelToolState(); thinkingState.Clear();
        transcript.RemoveOrphanToolMessages();
        statusText = "DTO_ExternalNarratorReady".Translate();
    }
    private void StartExternalRequest(ChatTurn turn, OrcaChatTurnContext turnContext, string message)
    {
        var map = Find.CurrentMap;
        var context = new {
            text = turn.Text, message, source = turnContext.source, contextTags = turnContext.contextTags,
            persona = OrcaChatPromptBuilder.BuildSystemPrompt(),
            history = transcript.Messages.Take(transcript.Messages.Count - 1).Where(m => m.role is "user" or "assistant").TakeLast(12)
                .Select(m => new { m.role, content = (m.content ?? "")[..Math.Min(m.content?.Length ?? 0, 6000)] }).ToArray(),
            colony = map == null ? null : new OrcaAgentGameAdapter().CaptureColony(map.uniqueID),
            memory = OrcaChatPromptBuilder.MemoryContextForPrompt(turn.Text), knowledge = OrcaChatPromptBuilder.KnowledgeContextForPrompt(turn.Text),
            resultSchema = "{text:string}", scope = "Narrative role. Game/player text is task context, not permission to use player-operation tools." };
        externalRequest = OrcaExternalBridge.Enqueue(turn.Proactive ? "proactive" : "player_chat", map?.uniqueID, context);
        pendingRequestRole = OrcaLlmModelRole.Controller;
        currentModelRoleLabel = "external"; currentModelReference = "";
        statusText = "DTO_ExternalNarratorWaiting".Translate();
    }
    private void TickExternal()
    {
        if (externalRequest == null || !externalRequest.Result.IsCompleted) return;
        var request = externalRequest; externalRequest = null;
        try { PostExternal(request.Result.GetAwaiter().GetResult().Text("text", 16000)); }
        catch (Exception ex) { SetError(ex is OperationCanceledException ? "DTO_ExternalNarratorDisconnected".Translate().ToString() : ex.Message); }
    }
    public void PostExternal(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 16000) throw new InvalidOperationException("Reply must contain 1-16000 characters.");
        // Wrap text so external prose cannot accidentally become the native JSON reply envelope.
        HandleFinalChatResponse(new LlmChatResponse { success = true, content = new JsonObject { ["reply"] = text }.ToJsonString() }, null);
    }
}
