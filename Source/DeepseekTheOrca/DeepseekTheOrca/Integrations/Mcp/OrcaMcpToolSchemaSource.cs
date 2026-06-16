using System.Collections.Generic;

namespace DeepseekTheOrca
{
    public sealed class OrcaMcpToolSchemaSource : ILlmToolSchemaSource
    {
        public void AppendToolSchemas(OrcaLlmModelRole role, HashSet<string> allowedToolNames, List<Dictionary<string, object>> tools)
        {
            if (role != OrcaLlmModelRole.Tool)
            {
                return;
            }

            List<OrcaMcpToolDescriptor> mcpTools = OrcaHttpMcpClient.DiscoverTools();
            for (int i = 0; i < mcpTools.Count; i++)
            {
                OrcaMcpToolDescriptor tool = mcpTools[i];
                if (allowedToolNames != null && !allowedToolNames.Contains(tool.exposedName))
                {
                    continue;
                }

                tools.Add(LlmToolSchemas.Function(tool.exposedName, tool.description, tool.inputSchema ?? LlmToolSchemas.EmptyParameters()));
            }
        }

        public bool IsToolAllowedForRole(OrcaLlmModelRole role, string toolName)
        {
            return role == OrcaLlmModelRole.Tool
                && toolName != "web_search"
                && OrcaHttpMcpClient.IsExposedTool(toolName);
        }
    }
}
