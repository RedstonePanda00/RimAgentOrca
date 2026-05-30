using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class AiToolCall
    {
        public string name;
        public readonly Dictionary<string, string> arguments = new Dictionary<string, string>();

        public AiToolCall(string name)
        {
            this.name = name;
        }
    }

    public sealed class AiToolResult
    {
        public bool success;
        public string message;
        public readonly Dictionary<string, string> values = new Dictionary<string, string>();

        public static AiToolResult Ok(string message)
        {
            return new AiToolResult { success = true, message = message };
        }

        public static AiToolResult Fail(string message)
        {
            return new AiToolResult { success = false, message = message };
        }

        public AiToolResult WithValue(string key, object value)
        {
            values[key] = value == null ? "" : value.ToString();
            return this;
        }
    }

    public interface IAiStoryTool
    {
        string Name { get; }
        string Description { get; }
        AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments);
    }

    public sealed class AiToolDefinition
    {
        public readonly IAiStoryTool tool;
        public readonly Dictionary<string, object> parameters;
        public readonly bool exposeToStorytellerPlanning;
        public readonly bool exposeToChat;
        public readonly bool requiresCurrentMap;
        public readonly bool allowDuringProactive;
        public readonly bool isExecutionTool;

        public AiToolDefinition(
            IAiStoryTool tool,
            Dictionary<string, object> parameters,
            bool exposeToStorytellerPlanning,
            bool exposeToChat,
            bool requiresCurrentMap,
            bool allowDuringProactive,
            bool isExecutionTool)
        {
            this.tool = tool;
            this.parameters = parameters;
            this.exposeToStorytellerPlanning = exposeToStorytellerPlanning;
            this.exposeToChat = exposeToChat;
            this.requiresCurrentMap = requiresCurrentMap;
            this.allowDuringProactive = allowDuringProactive;
            this.isExecutionTool = isExecutionTool;
        }

        public string Name
        {
            get { return tool == null ? "" : tool.Name; }
        }

        public string Description
        {
            get { return tool == null ? "" : tool.Description; }
        }
    }

    public sealed class AiToolContext
    {
        public readonly IIncidentTarget target;
        public readonly StorytellerComp source;
        public readonly StorytellerCompProperties_DeepseekOrca props;
        public readonly StringBuilder trace = new StringBuilder();

        public AiToolContext(IIncidentTarget target, StorytellerComp source, StorytellerCompProperties_DeepseekOrca props)
        {
            this.target = target;
            this.source = source;
            this.props = props;
        }

        public Map Map
        {
            get { return target as Map; }
        }

        public float ThreatPoints
        {
            get { return StorytellerUtility.DefaultThreatPointsNow(target); }
        }

        public void TraceTool(string toolName, AiToolResult result)
        {
            if (trace.Length > 0)
            {
                trace.AppendLine();
            }

            trace.Append(toolName).Append(": ").Append(result.success ? "ok" : "failed").Append(" - ").Append(result.message);
        }
    }

    public sealed class AiToolSession
    {
        private readonly AiToolContext context;
        private int callsUsed;

        public AiToolSession(AiToolContext context)
        {
            this.context = context;
        }

        public AiToolResult Invoke(string toolName, Dictionary<string, string> arguments)
        {
            callsUsed++;
            int maxCalls = DeepseekTheOrcaMod.Settings == null ? 8 : DeepseekTheOrcaMod.Settings.maxToolCalls;
            if (callsUsed > maxCalls)
            {
                return AiToolResult.Fail("tool call budget exceeded");
            }

            IAiStoryTool tool;
            if (!AiStoryToolRegistry.TryGet(toolName, out tool))
            {
                AiToolResult mcpResult;
                if (OrcaHttpMcpClient.TryInvokeExposedTool(toolName, arguments, out mcpResult))
                {
                    context.TraceTool(toolName, mcpResult);
                    return mcpResult;
                }

                return AiToolResult.Fail("unknown tool: " + toolName);
            }

            AiToolResult result;
            try
            {
                result = tool.Invoke(context, arguments ?? new Dictionary<string, string>());
            }
            catch (Exception ex)
            {
                result = AiToolResult.Fail(ex.GetType().Name + ": " + ex.Message);
            }

            context.TraceTool(toolName, result);
            return result;
        }
    }

    public sealed class DefBackedAiStoryTool : IAiStoryTool
    {
        private readonly OrcaToolDef def;

        public DefBackedAiStoryTool(OrcaToolDef def)
        {
            this.def = def;
        }

        public string Name
        {
            get { return def == null ? "" : def.ToolName; }
        }

        public string Description
        {
            get { return def == null ? "" : def.ToolDescription; }
        }

        public AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            if (def == null)
            {
                return AiToolResult.Fail("tool def is unavailable");
            }

            OrcaToolWorker worker = def.Worker;
            if (worker == null)
            {
                return AiToolResult.Fail("tool worker is unavailable: " + def.defName);
            }

            string reason;
            if (!worker.CanUse(context, out reason))
            {
                return AiToolResult.Fail(reason.NullOrEmpty() ? "tool cannot be used now" : reason);
            }

            return worker.Invoke(context, arguments ?? new Dictionary<string, string>());
        }
    }

    public static class AiStoryToolRegistry
    {
        private static readonly Dictionary<string, IAiStoryTool> tools = new Dictionary<string, IAiStoryTool>();
        private static readonly Dictionary<string, AiToolDefinition> definitions = new Dictionary<string, AiToolDefinition>();
        private static bool initialized;

        public static IEnumerable<IAiStoryTool> AllTools
        {
            get
            {
                EnsureInitialized();
                return tools.Values;
            }
        }

        public static IEnumerable<AiToolDefinition> AllDefinitions
        {
            get
            {
                EnsureInitialized();
                return definitions.Values;
            }
        }

        public static IEnumerable<AiToolDefinition> StorytellerPlanningDefinitions
        {
            get
            {
                EnsureInitialized();
                foreach (AiToolDefinition definition in definitions.Values)
                {
                    if (definition.exposeToStorytellerPlanning)
                    {
                        yield return definition;
                    }
                }
            }
        }

        public static IEnumerable<AiToolDefinition> ChatDefinitions
        {
            get
            {
                EnsureInitialized();
                foreach (AiToolDefinition definition in definitions.Values)
                {
                    if (definition.exposeToChat)
                    {
                        yield return definition;
                    }
                }
            }
        }

        public static bool TryGet(string name, out IAiStoryTool tool)
        {
            EnsureInitialized();
            return tools.TryGetValue(name, out tool);
        }

        public static bool TryGetDefinition(string name, out AiToolDefinition definition)
        {
            EnsureInitialized();
            return definitions.TryGetValue(name, out definition);
        }

        public static bool IsExposedToChat(string name)
        {
            AiToolDefinition definition;
            return TryGetDefinition(name, out definition) && definition.exposeToChat;
        }

        public static bool IsExecutionTool(string name)
        {
            AiToolDefinition definition;
            return TryGetDefinition(name, out definition) && definition.isExecutionTool;
        }

        public static bool RequiresCurrentMap(string name)
        {
            AiToolDefinition definition;
            return !TryGetDefinition(name, out definition) || definition.requiresCurrentMap;
        }

        public static bool AllowsDuringProactive(string name)
        {
            AiToolDefinition definition;
            return !TryGetDefinition(name, out definition) || definition.allowDuringProactive;
        }

        public static void Reset()
        {
            initialized = false;
            tools.Clear();
            definitions.Clear();
        }

        private static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            RegisterDefTools();
        }

        private static void RegisterDefTools()
        {
            List<OrcaToolDef> defs = DefDatabase<OrcaToolDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                OrcaToolDef def = defs[i];
                if (def == null)
                {
                    continue;
                }

                bool enabled = DeepseekTheOrcaMod.Settings == null
                    ? def.defaultEnabled
                    : DeepseekTheOrcaMod.Settings.IsDefToolEnabled(def.defName, def.defaultEnabled);
                if (!enabled)
                {
                    continue;
                }

                OrcaToolWorker worker = def.Worker;
                if (worker == null)
                {
                    Log.Warning("[RimAgent] Skipping OrcaToolDef " + def.defName + " because its worker is unavailable.");
                    continue;
                }
                if (!worker.ShouldRegister())
                {
                    continue;
                }

                DefBackedAiStoryTool tool = new DefBackedAiStoryTool(def);
                if (tool.Name.NullOrEmpty())
                {
                    continue;
                }

                if (tools.ContainsKey(tool.Name))
                {
                    Log.Warning("[RimAgent] Skipping OrcaToolDef " + def.defName + " because tool name is already registered: " + tool.Name);
                    continue;
                }

                Register(tool, def.BuildInputSchema(), def.exposeToStorytellerPlanning, def.exposeToChat, def.requiresCurrentMap, def.allowDuringProactive, def.isExecutionTool);
            }
        }

        private static void Register(
            IAiStoryTool tool,
            Dictionary<string, object> parameters,
            bool exposeToStorytellerPlanning,
            bool exposeToChat,
            bool requiresCurrentMap = true,
            bool allowDuringProactive = true,
            bool isExecutionTool = false)
        {
            tools[tool.Name] = tool;
            definitions[tool.Name] = new AiToolDefinition(tool, parameters, exposeToStorytellerPlanning, exposeToChat, requiresCurrentMap, allowDuringProactive, isExecutionTool);
        }
    }

}
