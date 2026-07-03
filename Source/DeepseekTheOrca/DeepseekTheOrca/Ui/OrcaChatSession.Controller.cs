using System.Collections.Generic;
using Verse;

namespace DeepseekTheOrca
{
    public sealed partial class OrcaChatSession
    {
        private void StartControllerOrChatRequest(DeepseekTheOrcaSettings settings)
        {
            OrcaLocalRouteDecision localDecision = OrcaLocalRouteGate.Decide(lastUserText, settings, allowExecutionToolsThisTurn);
            if (settings != null && settings.HasModelForRole(OrcaLlmModelRole.Controller))
            {
                AddProcess("Routing through controller model as central router. Local hint: " + localDecision.route + " (" + localDecision.reason + ").");
                StartControllerRequest(settings);
                return;
            }

            OrcaAgentRoutingContext routingContext = new OrcaAgentRoutingContext(this, localDecision.route, localDecision.role, "local route");
            OrcaExtensionManager.ModifyAgentRouting(routingContext);
            lastControllerRoute = "local:" + routingContext.route;
            ForceNextModelRole(routingContext.requestedRole);
            AddProcess("Local route: " + routingContext.route + " -> " + OrcaChatRoleUtility.ModelRoleLabel(routingContext.requestedRole) + " model (" + localDecision.reason + ").");
            if (routingContext.Changed)
            {
                AddProcess("Extension adjusted local route to " + routingContext.route + " -> " + OrcaChatRoleUtility.ModelRoleLabel(routingContext.requestedRole) + " model.");
            }
            StartRequest(settings);
        }

        private void StartControllerRequest(DeepseekTheOrcaSettings settings)
        {
            pendingStage = OrcaChatRequestStage.Controller;
            thinkingState.Ensure(transcript.DisplayLines, OrcaChatPromptBuilder.CurrentPersonaSpeakerName(), MarkConversationChanged);
            pendingRequest = client.SendPlainChatCompletionAsync(settings, OrcaChatControllerRouter.BuildControllerMessages(this, transcript.Messages, lastUserText), OrcaLlmModelRole.Controller);
            currentModelRoleLabel = OrcaChatRoleUtility.ModelRoleLabel(OrcaLlmModelRole.Controller);
            currentModelReference = settings.ModelForRole(OrcaLlmModelRole.Controller);
            AddProcess("Request sent to controller model: " + settings.ModelForRole(OrcaLlmModelRole.Controller));
            NotifyAgentPhase(OrcaAgentPhase.Routing, OrcaLlmModelRole.Controller, false, "controller request sent");
        }

        private void StartControllerReviewRequest(DeepseekTheOrcaSettings settings)
        {
            pendingStage = OrcaChatRequestStage.ControllerReview;
            thinkingState.Ensure(transcript.DisplayLines, OrcaChatPromptBuilder.CurrentPersonaSpeakerName(), MarkConversationChanged);
            pendingRequest = client.SendPlainChatCompletionAsync(
                settings,
                OrcaChatControllerRouter.BuildControllerReviewMessages(
                    this,
                    transcript.Messages,
                    lastUserText,
                    toolRoundsUsed,
                    MaxToolGatheringRounds,
                    toolCallsUsedThisTurn,
                    MaxToolCallsForSettings(settings),
                    specialistReturnedNoToolCalls),
                OrcaLlmModelRole.Controller);
            currentModelRoleLabel = OrcaChatRoleUtility.ModelRoleLabel(OrcaLlmModelRole.Controller);
            currentModelReference = settings.ModelForRole(OrcaLlmModelRole.Controller);
            AddProcess("Tool results returned to controller model for review: " + settings.ModelForRole(OrcaLlmModelRole.Controller));
            NotifyAgentPhase(OrcaAgentPhase.Routing, OrcaLlmModelRole.Controller, false, "controller review request sent");
        }

        private void HandleControllerResponse(LlmChatResponse response)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !OrcaChatRoleUtility.HasAnyChatModel(settings))
            {
                statusText = "DTO_OrcaChatNoApiKey".Translate();
                SetError(statusText);
                return;
            }

            OrcaLlmModelRole role;
            OrcaControllerDecision decision = OrcaChatControllerRouter.ResolveDecision(response.content, settings, out role);
            OrcaAgentRoutingContext routingContext = new OrcaAgentRoutingContext(this, decision.route, role, "controller route");
            OrcaExtensionManager.ModifyAgentRouting(routingContext);
            string route = routingContext.route;
            role = routingContext.requestedRole;
            lastControllerRoute = route;
            ForceNextModelRole(role);
            ApplyControllerSkillSelection(decision.skillIds);
            ApplyControllerContextSummary(decision.contextSummary, "controller route");
            AddProcess("Controller route: " + route + " -> " + OrcaChatRoleUtility.ModelRoleLabel(role) + " model.");
            if (routingContext.Changed)
            {
                AddProcess("Extension adjusted route to " + route + " -> " + OrcaChatRoleUtility.ModelRoleLabel(role) + " model.");
            }

            StartRequest(settings);
        }

        private void HandleControllerReviewResponse(LlmChatResponse response)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !OrcaChatRoleUtility.HasAnyChatModel(settings))
            {
                statusText = "DTO_OrcaChatNoApiKey".Translate();
                SetError(statusText);
                return;
            }

            OrcaLlmModelRole role;
            OrcaControllerDecision decision = OrcaChatControllerRouter.ResolveDecision(response.content, settings, out role);
            OrcaAgentRoutingContext routingContext = new OrcaAgentRoutingContext(this, decision.route, role, "controller review route");
            OrcaExtensionManager.ModifyAgentRouting(routingContext);
            string route = routingContext.route;
            role = routingContext.requestedRole;

            if (role != OrcaLlmModelRole.Dialogue && !CanContinueSpecialistGathering(settings))
            {
                AddProcess("Controller review requested " + route + ", but the gathering budget is exhausted; forcing dialogue model.");
                ContinueToDialogueWithToolBudgetExhausted("controller review requested more specialist data after budget was exhausted");
                return;
            }

            if (role != OrcaLlmModelRole.Dialogue && specialistReturnedNoToolCalls)
            {
                AddProcess("Controller review requested " + route + ", but the previous specialist produced no tool calls; forcing dialogue model.");
                RouteToDialogueAfterControllerReview(settings, "previous specialist produced no tool calls");
                return;
            }

            lastControllerRoute = "review:" + route;
            specialistReturnedNoToolCalls = false;
            ApplyControllerContextSummary(decision.contextSummary, "controller review");
            AddProcess("Controller review route: " + route + " -> " + OrcaChatRoleUtility.ModelRoleLabel(role) + " model.");
            if (routingContext.Changed)
            {
                AddProcess("Extension adjusted controller review route to " + route + " -> " + OrcaChatRoleUtility.ModelRoleLabel(role) + " model.");
            }

            if (role == OrcaLlmModelRole.Dialogue)
            {
                RouteToDialogueAfterControllerReview(settings, "controller review found enough context");
                return;
            }

            transcript.AddMessage(LlmChatMessage.System(
                "Controller review requested more specialist data from the "
                + OrcaChatRoleUtility.ModelRoleLabel(role)
                + " model. Continue gathering data only; do not write player-facing prose."));
            ForceNextModelRole(role);
            StartRequest(settings);
        }

        private void ApplyControllerSkillSelection(IEnumerable<string> skillIds)
        {
            List<string> selected = OrcaSkillManager.ValidEnabledSkillIds(skillIds);
            if (selected.Count > 0)
            {
                AddProcess("Controller selected skill(s): " + string.Join(", ", selected.ToArray()));
            }

            int userIndex = transcript.LatestUserMessageIndex();
            if (userIndex < 0)
            {
                return;
            }

            List<string> contextTags = OrcaChatPromptBuilder.PlayerContextTags(lastUserText);
            OrcaChatTurnContext turnContext = new OrcaChatTurnContext(this, "player_chat", lastPlayerName, lastUserText, contextTags, false);
            transcript.Messages[userIndex].content = OrcaChatPromptBuilder.BuildPlayerMessage(turnContext, selected);
        }

        private void ApplyControllerContextSummary(string contextSummary, string source)
        {
            if (contextSummary.NullOrEmpty())
            {
                return;
            }

            transcript.AddMessage(LlmChatMessage.System(
                "Controller context summary for downstream models (" + source + "):\n"
                + contextSummary.Trim()));
        }

        private void StartControllerReviewOrDialogue(DeepseekTheOrcaSettings settings, string reason)
        {
            if (settings != null
                && settings.HasModelForRole(OrcaLlmModelRole.Controller)
                && CanContinueSpecialistGathering(settings))
            {
                StartControllerReviewRequest(settings);
                return;
            }

            AddProcess("Skipping controller review; routing to dialogue model. Reason: " + reason + ".");
            RouteToDialogueAfterControllerReview(settings, reason);
        }

        private bool CanContinueSpecialistGathering(DeepseekTheOrcaSettings settings)
        {
            return settings != null
                && toolRoundsUsed < MaxToolGatheringRounds
                && toolCallsUsedThisTurn < MaxToolCallsForSettings(settings);
        }

        private int MaxToolCallsForSettings(DeepseekTheOrcaSettings settings)
        {
            return settings == null ? 8 : settings.maxToolCalls;
        }

        private void RouteToDialogueAfterControllerReview(DeepseekTheOrcaSettings settings, string reason)
        {
            if (settings == null || !settings.HasModelForRole(OrcaLlmModelRole.Dialogue))
            {
                statusText = "DTO_OrcaChatNoApiKey".Translate();
                SetError(statusText);
                return;
            }

            transcript.AddMessage(LlmChatMessage.System(
                "Controller review is complete. Reason: " + reason + ". "
                + "The next assistant response must be exactly one JSON object and no extra text. "
                + "JSON schema: " + OrcaChatPromptBuilder.ChatReplyJsonSchema() + "."));
            ForceNextModelRole(OrcaLlmModelRole.Dialogue);
            StartRequest(settings);
        }

    }
}
