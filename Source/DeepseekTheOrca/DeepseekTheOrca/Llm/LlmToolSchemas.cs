using System.Collections.Generic;

namespace DeepseekTheOrca
{
    // Tool schema providers register here at startup (see DeepseekTheOrcaMod)
    // so the Llm layer never references upper-layer tool registries directly.
    public interface ILlmToolSchemaSource
    {
        void AppendToolSchemas(OrcaLlmModelRole role, HashSet<string> allowedToolNames, List<Dictionary<string, object>> tools);
        bool IsToolAllowedForRole(OrcaLlmModelRole role, string toolName);
    }

    public static class LlmToolSchemas
    {
        private static readonly List<ILlmToolSchemaSource> sources = new List<ILlmToolSchemaSource>();

        public static void RegisterSource(ILlmToolSchemaSource source)
        {
            if (source != null && !sources.Contains(source))
            {
                sources.Add(source);
            }
        }

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
            return BuildForRole(role, null);
        }

        public static List<Dictionary<string, object>> BuildForRole(OrcaLlmModelRole role, HashSet<string> allowedToolNames)
        {
            List<Dictionary<string, object>> tools = new List<Dictionary<string, object>>();
            for (int i = 0; i < sources.Count; i++)
            {
                sources[i].AppendToolSchemas(role, allowedToolNames, tools);
            }

            return tools;
        }

        public static bool IsToolAllowedForRole(OrcaLlmModelRole role, string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
            {
                return false;
            }

            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].IsToolAllowedForRole(role, toolName))
                {
                    return true;
                }
            }

            return false;
        }

        public static Dictionary<string, object> EmptyParameters()
        {
            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>() }
            };
        }

        public static Dictionary<string, object> Function(string name, string description, Dictionary<string, object> parameters)
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
