using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaChatRoleUtility
    {
        public static bool ShouldStreamFinalReply(OrcaLlmModelRole role)
        {
            return role == OrcaLlmModelRole.Dialogue;
        }

        public static bool IsToolGatheringRole(OrcaLlmModelRole role)
        {
            return role == OrcaLlmModelRole.Tool
                || role == OrcaLlmModelRole.WebSearch
                || role == OrcaLlmModelRole.Vision;
        }

        public static OrcaAgentPhase PhaseForRole(OrcaLlmModelRole role)
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

        public static bool HasAnyChatModel(DeepseekTheOrcaSettings settings)
        {
            return settings != null
                && (settings.HasModelForRole(OrcaLlmModelRole.Dialogue)
                    || settings.HasModelForRole(OrcaLlmModelRole.Tool)
                    || settings.HasModelForRole(OrcaLlmModelRole.WebSearch)
                    || settings.HasModelForRole(OrcaLlmModelRole.Vision));
        }

        public static OrcaLlmModelRole InitialChatModelRole(DeepseekTheOrcaSettings settings)
        {
            if (settings != null && !settings.toolModel.NullOrEmpty())
            {
                return OrcaLlmModelRole.Tool;
            }

            return FirstAvailableChatModelRole(settings);
        }

        public static OrcaLlmModelRole FirstAvailableChatModelRole(DeepseekTheOrcaSettings settings)
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

        public static string ModelRoleLabel(OrcaLlmModelRole role)
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
                case OrcaLlmModelRole.Embedding:
                    return "embedding";
                case OrcaLlmModelRole.Memory:
                    return "memory";
                default:
                    return "fallback";
            }
        }
    }
}
