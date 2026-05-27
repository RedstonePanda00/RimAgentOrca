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
                    LlmToolSchemas.BuildForRole(role),
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

        private static bool IsToolGatheringRole(OrcaLlmModelRole role)
        {
            return role == OrcaLlmModelRole.Tool
                || role == OrcaLlmModelRole.WebSearch
                || role == OrcaLlmModelRole.Vision;
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
            string skillSelectionCatalog = OrcaSkillManager.FormatSkillSelectionCatalog();
            string pluginRoutingHint = DeepseekTheOrcaMod.FormatPluginControllerRoutingHint();
            controllerMessages.Add(LlmChatMessage.System(
                "You are the chat controller model. Route the latest RimWorld chat turn to exactly one specialist. "
                + "Return exactly one JSON object and no extra text. "
                + "Schema: {\"route\":\"dialogue|tool|web_search|vision\",\"skillIds\":[\"enabled skill id\"],\"reason\":\"short reason\"}. "
                + "Use dialogue for ordinary conversation and final wording. "
                + "Use tool when current game state, pawns, incidents, RimTalk history, MCP tools, or event execution may be needed. "
                + "Use web_search only for current external public-web information outside the game. "
                + "Use vision only when the request clearly depends on image recognition. "
                + "Select skillIds only from the enabled skill catalog, based on each skill description and the latest player request. "
                + "Use an empty skillIds array when no skill is directly relevant. "
                + "If unsure, choose tool when game state might matter, otherwise dialogue. "
                + AppendControllerHint(skillRoutingHint)
                + AppendControllerHint(skillSelectionCatalog)
                + AppendControllerHint(pluginRoutingHint)));
            controllerMessages.Add(LlmChatMessage.User(lastUserText.NullOrEmpty() ? latestUserContent : lastUserText));
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

            OrcaControllerDecision decision = ParseControllerDecision(response.content);
            OrcaLlmModelRole role = ModelRoleForControllerRoute(decision.route, settings);
            OrcaAgentRoutingContext routingContext = new OrcaAgentRoutingContext(this, decision.route, role, "controller route");
            OrcaExtensionManager.ModifyAgentRouting(routingContext);
            string route = routingContext.route;
            role = routingContext.requestedRole;
            lastControllerRoute = route;
            ForceNextModelRole(role);
            ApplyControllerSkillSelection(decision.skillIds);
            AddProcess("Controller route: " + route + " -> " + ModelRoleLabel(role) + " model.");
            if (routingContext.Changed)
            {
                AddProcess("Extension adjusted route to " + route + " -> " + ModelRoleLabel(role) + " model.");
            }
            StartRequest(settings);
        }

        private sealed class OrcaControllerDecision
        {
            public string route = "dialogue";
            public readonly List<string> skillIds = new List<string>();
        }

        private static OrcaControllerDecision ParseControllerDecision(string content)
        {
            OrcaControllerDecision decision = new OrcaControllerDecision();
            try
            {
                Dictionary<string, object> parsed = MiniJson.Deserialize(ExtractJsonObject(content ?? "")) as Dictionary<string, object>;
                string route = GetParsedString(parsed, "route");
                if (!route.NullOrEmpty())
                {
                    decision.route = route.Trim().ToLowerInvariant();
                }

                object skillIdsObj;
                List<object> skillIds = parsed != null && parsed.TryGetValue("skillIds", out skillIdsObj) ? skillIdsObj as List<object> : null;
                if (skillIds == null && parsed != null && parsed.TryGetValue("skill_ids", out skillIdsObj))
                {
                    skillIds = skillIdsObj as List<object>;
                }
                if (skillIds != null)
                {
                    for (int i = 0; i < skillIds.Count; i++)
                    {
                        string id = skillIds[i] == null ? "" : skillIds[i].ToString().Trim();
                        if (!id.NullOrEmpty() && !decision.skillIds.Contains(id))
                        {
                            decision.skillIds.Add(id);
                        }
                    }
                }
            }
            catch
            {
            }

            return decision;
        }

        private static string AppendControllerHint(string hint)
        {
            if (hint.NullOrEmpty())
            {
                return "";
            }

            return "\n" + hint.Trim() + "\n";
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

        private void ApplyControllerSkillSelection(IEnumerable<string> skillIds)
        {
            List<string> selected = OrcaSkillManager.ValidEnabledSkillIds(skillIds);
            if (selected.Count > 0)
            {
                AddProcess("Controller selected skill(s): " + string.Join(", ", selected.ToArray()));
            }

            int userIndex = LatestUserMessageIndex();
            if (userIndex < 0)
            {
                return;
            }

            messages[userIndex].content = BuildPlayerMessage(lastPlayerName, lastUserText, PlayerContextTags(lastUserText), selected);
        }

        private int LatestUserMessageIndex()
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

        private static OrcaLlmModelRole ModelRoleForControllerRoute(string route, DeepseekTheOrcaSettings settings)
        {
            if (route == "web_search")
            {
                return settings != null && settings.UsesLocalWebSearchTool
                    ? OrcaLlmModelRole.WebSearch
                    : OrcaLlmModelRole.Dialogue;
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
                else if (!LlmToolSchemas.IsToolAllowedForRole(pendingRequestRole, toolCall.name))
                {
                    result = AiToolResult.Fail("tool is not available to " + ModelRoleLabel(pendingRequestRole) + " model: " + toolCall.name);
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
            OrcaLlmModelRole nextRole = NextRoleAfterToolResults(settings);
            if (settings == null || !settings.HasModelForRole(nextRole))
            {
                statusText = "DTO_OrcaChatNoApiKey".Translate();
                SetError(statusText);
                return;
            }

            if (nextRole == OrcaLlmModelRole.Tool || nextRole == OrcaLlmModelRole.WebSearch)
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

        private OrcaLlmModelRole NextRoleAfterToolResults(DeepseekTheOrcaSettings settings)
        {
            if (toolRoundsUsed >= MaxToolRounds || settings == null)
            {
                return OrcaLlmModelRole.Dialogue;
            }

            if (pendingRequestRole == OrcaLlmModelRole.WebSearch && settings.HasModelForRole(OrcaLlmModelRole.WebSearch))
            {
                return OrcaLlmModelRole.WebSearch;
            }

            if (pendingRequestRole == OrcaLlmModelRole.Tool && settings.HasModelForRole(OrcaLlmModelRole.Tool))
            {
                return OrcaLlmModelRole.Tool;
            }

            return OrcaLlmModelRole.Dialogue;
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

    }
}
