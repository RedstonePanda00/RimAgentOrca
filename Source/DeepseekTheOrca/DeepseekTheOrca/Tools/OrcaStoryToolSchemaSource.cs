using System.Collections.Generic;

namespace DeepseekTheOrca
{
    public sealed class OrcaStoryToolSchemaSource : ILlmToolSchemaSource
    {
        public void AppendToolSchemas(OrcaLlmModelRole role, HashSet<string> allowedToolNames, List<Dictionary<string, object>> tools)
        {
            switch (role)
            {
                case OrcaLlmModelRole.Decision:
                    AppendStorytellerPlanningTools(tools);
                    break;
                case OrcaLlmModelRole.Tool:
                    AppendChatTools(allowedToolNames, tools);
                    break;
                case OrcaLlmModelRole.WebSearch:
                    AppendWebSearchTools(tools);
                    break;
            }
        }

        public bool IsToolAllowedForRole(OrcaLlmModelRole role, string toolName)
        {
            if (role == OrcaLlmModelRole.Decision)
            {
                AiToolDefinition definition;
                return AiStoryToolRegistry.TryGetDefinition(toolName, out definition) && definition.exposeToStorytellerPlanning;
            }

            if (role == OrcaLlmModelRole.WebSearch)
            {
                return toolName == "web_search" && WebSearchEnabled();
            }

            if (role == OrcaLlmModelRole.Tool)
            {
                if (toolName == "web_search")
                {
                    return false;
                }

                AiToolDefinition definition;
                return AiStoryToolRegistry.TryGetDefinition(toolName, out definition) && definition.exposeToChat;
            }

            return false;
        }

        private static void AppendStorytellerPlanningTools(List<Dictionary<string, object>> tools)
        {
            // The OrcaToolDef exposeToStorytellerPlanning XML flag is the single
            // source of truth for which tools the cycle planner may call.
            foreach (AiToolDefinition definition in AiStoryToolRegistry.StorytellerPlanningDefinitions)
            {
                tools.Add(LlmToolSchemas.Function(definition.Name, definition.Description, definition.parameters ?? LlmToolSchemas.EmptyParameters()));
            }
        }

        private static void AppendChatTools(HashSet<string> allowedToolNames, List<Dictionary<string, object>> tools)
        {
            foreach (AiToolDefinition definition in AiStoryToolRegistry.ChatDefinitions)
            {
                if (definition.Name == "web_search")
                {
                    continue;
                }
                if (allowedToolNames != null && !allowedToolNames.Contains(definition.Name))
                {
                    continue;
                }

                tools.Add(LlmToolSchemas.Function(definition.Name, definition.Description, definition.parameters ?? LlmToolSchemas.EmptyParameters()));
            }
        }

        private static void AppendWebSearchTools(List<Dictionary<string, object>> tools)
        {
            if (!WebSearchEnabled())
            {
                return;
            }

            AiToolDefinition definition;
            if (AiStoryToolRegistry.TryGetDefinition("web_search", out definition) && definition.exposeToChat)
            {
                tools.Add(LlmToolSchemas.Function(definition.Name, definition.Description, definition.parameters ?? LlmToolSchemas.EmptyParameters()));
            }
        }

        private static bool WebSearchEnabled()
        {
            return DeepseekTheOrcaMod.Settings != null && DeepseekTheOrcaMod.Settings.UsesLocalWebSearchTool;
        }
    }
}
