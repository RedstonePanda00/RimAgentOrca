using System.Collections.Generic;

namespace DeepseekTheOrca
{
    public static class LlmToolSchemas
    {
        public static List<Dictionary<string, object>> Build()
        {
            return BuildForRole(OrcaLlmModelRole.Decision);
        }

        public static List<Dictionary<string, object>> BuildChatTools()
        {
            return BuildForRole(OrcaLlmModelRole.Tool);
        }

        public static List<Dictionary<string, object>> BuildForRole(OrcaLlmModelRole role)
        {
            switch (role)
            {
                case OrcaLlmModelRole.Decision:
                    return BuildStorytellerPlanningTools();
                case OrcaLlmModelRole.Tool:
                    return BuildToolModelTools();
                case OrcaLlmModelRole.WebSearch:
                    return BuildWebSearchTools();
                case OrcaLlmModelRole.Vision:
                    return BuildVisionTools();
                default:
                    return new List<Dictionary<string, object>>();
            }
        }

        public static bool IsToolAllowedForRole(OrcaLlmModelRole role, string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
            {
                return false;
            }

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
                if (AiStoryToolRegistry.TryGetDefinition(toolName, out definition))
                {
                    return definition.exposeToChat;
                }

                return OrcaHttpMcpClient.IsExposedTool(toolName);
            }

            return false;
        }

        private static List<Dictionary<string, object>> BuildStorytellerPlanningTools()
        {
            List<Dictionary<string, object>> tools = new List<Dictionary<string, object>>();
            foreach (AiToolDefinition definition in AiStoryToolRegistry.StorytellerPlanningDefinitions)
            {
                tools.Add(Function(definition.Name, definition.Description, definition.parameters ?? EmptyParameters()));
            }
            return tools;
        }

        private static List<Dictionary<string, object>> BuildToolModelTools()
        {
            List<Dictionary<string, object>> tools = new List<Dictionary<string, object>>();
            foreach (AiToolDefinition definition in AiStoryToolRegistry.ChatDefinitions)
            {
                if (definition.Name == "web_search")
                {
                    continue;
                }

                tools.Add(Function(definition.Name, definition.Description, definition.parameters ?? EmptyParameters()));
            }
            AppendHttpMcpTools(tools);
            return tools;
        }

        private static List<Dictionary<string, object>> BuildWebSearchTools()
        {
            List<Dictionary<string, object>> tools = new List<Dictionary<string, object>>();
            if (!WebSearchEnabled())
            {
                return tools;
            }

            AiToolDefinition definition;
            if (AiStoryToolRegistry.TryGetDefinition("web_search", out definition) && definition.exposeToChat)
            {
                tools.Add(Function(definition.Name, definition.Description, definition.parameters ?? EmptyParameters()));
            }

            return tools;
        }

        private static List<Dictionary<string, object>> BuildVisionTools()
        {
            return new List<Dictionary<string, object>>();
        }

        public static Dictionary<string, object> EmptyParameters()
        {
            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>() }
            };
        }

        private static void AppendHttpMcpTools(List<Dictionary<string, object>> tools)
        {
            List<OrcaMcpToolDescriptor> mcpTools = OrcaHttpMcpClient.DiscoverTools();
            for (int i = 0; i < mcpTools.Count; i++)
            {
                OrcaMcpToolDescriptor tool = mcpTools[i];
                tools.Add(Function(tool.exposedName, tool.description, tool.inputSchema ?? EmptyParameters()));
            }
        }

        private static bool WebSearchEnabled()
        {
            return DeepseekTheOrcaMod.Settings != null && DeepseekTheOrcaMod.Settings.UsesLocalWebSearchTool;
        }

        private static Dictionary<string, object> Function(string name, string description, Dictionary<string, object> parameters)
        {
            return new Dictionary<string, object>
            {
                { "type", "function" },
                { "function", new Dictionary<string, object>
                    {
                        { "name", name },
                        { "description", description },
                        { "parameters", parameters }
                    }
                }
            };
        }
    }
}
