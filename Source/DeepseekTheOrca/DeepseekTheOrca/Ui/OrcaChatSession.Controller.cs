using System.Collections.Generic;
using Verse;

namespace DeepseekTheOrca
{
    public sealed partial class OrcaChatSession
    {
        private void StartControllerOrChatRequest(DeepseekTheOrcaSettings settings)
        {
            OrcaLocalRouteDecision localDecision = OrcaLocalRouteGate.Decide(lastUserText, settings, allowExecutionToolsThisTurn);
            if (localDecision.useController && settings != null && settings.HasModelForRole(OrcaLlmModelRole.Controller))
            {
                AddProcess("Local route gate deferred to controller: " + localDecision.reason + ".");
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
            thinkingState.Ensure(displayLines, OrcaChatPromptBuilder.CurrentPersonaSpeakerName(), MarkConversationChanged);
            pendingRequest = client.SendPlainChatCompletionAsync(settings, OrcaChatControllerRouter.BuildControllerMessages(messages, lastUserText), OrcaLlmModelRole.Controller);
            currentModelRoleLabel = OrcaChatRoleUtility.ModelRoleLabel(OrcaLlmModelRole.Controller);
            currentModelReference = settings.ModelForRole(OrcaLlmModelRole.Controller);
            AddProcess("Request sent to controller model: " + settings.ModelForRole(OrcaLlmModelRole.Controller));
            NotifyAgentPhase(OrcaAgentPhase.Routing, OrcaLlmModelRole.Controller, false, "controller request sent");
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

            OrcaControllerDecision decision = OrcaChatControllerRouter.ParseDecision(response.content);
            OrcaLlmModelRole role = OrcaChatControllerRouter.ModelRoleForRoute(decision.route, settings);
            OrcaAgentRoutingContext routingContext = new OrcaAgentRoutingContext(this, decision.route, role, "controller route");
            OrcaExtensionManager.ModifyAgentRouting(routingContext);
            string route = routingContext.route;
            role = routingContext.requestedRole;
            lastControllerRoute = route;
            ForceNextModelRole(role);
            ApplyControllerSkillSelection(decision.skillIds);
            AddProcess("Controller route: " + route + " -> " + OrcaChatRoleUtility.ModelRoleLabel(role) + " model.");
            if (routingContext.Changed)
            {
                AddProcess("Extension adjusted route to " + route + " -> " + OrcaChatRoleUtility.ModelRoleLabel(role) + " model.");
            }

            StartRequest(settings);
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

            List<string> contextTags = OrcaChatPromptBuilder.PlayerContextTags(lastUserText);
            OrcaChatTurnContext turnContext = new OrcaChatTurnContext(this, "player_chat", lastPlayerName, lastUserText, contextTags, false);
            messages[userIndex].content = OrcaChatPromptBuilder.BuildPlayerMessage(turnContext, selected);
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

    }
}
