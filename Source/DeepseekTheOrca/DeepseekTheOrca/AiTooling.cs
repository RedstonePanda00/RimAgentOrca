using System;
using System.Collections.Generic;
using System.Text;
using DeepseekTheOrca.Rimtalk;
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

    public static class AiStoryToolRegistry
    {
        private static readonly Dictionary<string, IAiStoryTool> tools = new Dictionary<string, IAiStoryTool>();
        private static bool initialized;

        public static IEnumerable<IAiStoryTool> AllTools
        {
            get
            {
                EnsureInitialized();
                return tools.Values;
            }
        }

        public static bool TryGet(string name, out IAiStoryTool tool)
        {
            EnsureInitialized();
            return tools.TryGetValue(name, out tool);
        }

        private static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            Register(new GetColonySummaryTool());
            Register(new GetRecentLettersTool());
            Register(new ListMapPawnsTool());
            Register(new GetPawnDetailsTool());
            Register(new ListAvailableIncidentsTool());
            Register(new CanFireIncidentTool());
            Register(new ProposeIncidentTool());
            Register(new ScheduleIncidentTool());
            Register(new TriggerRaidTool());
            Register(new SpawnPawnsTool());
            Register(new WebSearchTool());
            if (RimtalkIntegration.IsAvailable)
            {
                Register(new GetRimtalkChatHistoryTool());
            }
        }

        private static void Register(IAiStoryTool tool)
        {
            tools[tool.Name] = tool;
        }
    }

}
