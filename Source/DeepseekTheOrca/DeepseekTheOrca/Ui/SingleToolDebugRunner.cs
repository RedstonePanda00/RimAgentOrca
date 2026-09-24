using System.Collections.Generic;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
    public static class SingleToolDebugRunner
    {
        private static readonly List<string> logLines = new List<string>();
        private static Task<AiToolResult> pending;
        private static AiToolContext pendingContext;
        private static Game owner;
        private static string pendingTool;
        private static Dictionary<string, string> pendingArguments;

        public static IEnumerable<string> LogLines
        {
            get { return logLines; }
        }

        public static void Run(string toolName, Dictionary<string, string> arguments)
        {
            Update();
            if (pending != null) { AddLog("A debug tool is still running."); return; }
            if (Find.CurrentMap == null)
            {
                AddLog("No current map.");
                return;
            }

            arguments = arguments == null ? new Dictionary<string, string>() : new Dictionary<string, string>(arguments);

            AiToolContext context = new AiToolContext(Find.CurrentMap, null, null);
            AiToolSession session = new AiToolSession(context);

            AddLog("Run tool: " + toolName + " " + FormatArguments(arguments));
            owner = Current.Game;
            pendingTool = toolName;
            pendingArguments = arguments;
            pendingContext = context;
            pending = session.BeginInvoke(toolName, arguments);
            Update();
        }

        internal static void Update()
        {
            if (pending == null) return;
            if (owner != Current.Game) { Reset(); return; }
            if (!pending.IsCompleted) return;
            var context = pendingContext;
            var arguments = pendingArguments;
            var toolName = pendingTool;
            var finished = pending;
            Reset();
            AiToolResult result = finished.IsCanceled ? AiToolResult.Fail("Debug tool cancelled.")
                : finished.IsFaulted ? AiToolResult.Fail(finished.Exception.GetBaseException().Message)
                : finished.Result ?? AiToolResult.Fail("Debug tool returned no result.");
            AddLog("Result: " + (result.success ? "ok" : "failed") + " - " + result.message + FormatValues(result));

            if (context.trace.Length > 0)
            {
                AddLog("Trace: " + context.trace.ToString().Replace("\n", " | "));
            }

            if (!ReferenceEquals(context.target, Find.CurrentMap)) return;

            if (result.success && toolName == "schedule_incident")
            {
                FireScheduledIncident(arguments);
            }
            else if (result.success && toolName == "trigger_raid")
            {
                FireTriggeredRaid(arguments);
            }
            else if (result.success && toolName == "spawn_pawns")
            {
                SpawnPawns(arguments);
            }
        }

        internal static void Reset()
        {
            pending = null;
            pendingContext = null;
            pendingArguments = null;
            pendingTool = null;
            owner = null;
        }

        public static void ClearLog()
        {
            logLines.Clear();
        }

        private static void AddLog(string line)
        {
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            logLines.Add("tick " + tick + " | " + line);
            while (logLines.Count > 200)
            {
                logLines.RemoveAt(0);
            }
        }

        private static void FireScheduledIncident(Dictionary<string, string> arguments)
        {
            StorytellerComp_DeepseekOrca comp = OrcaStorytellerUtility.ActiveOrcaComp();
            if (comp == null)
            {
                AddLog("Execute: failed - active storyteller does not contain StorytellerComp_DeepseekOrca");
                return;
            }

            AiIncidentPlan plan;
            string rejectReason;
            if (!OrcaStorytellerUtility.TryBuildIncidentPlan(arguments, "manual single tool debug", out plan, out rejectReason))
            {
                AddLog("Execute: failed - " + rejectReason);
                return;
            }

            string message;
            string traceText;
            bool fired = comp.TryFireIncidentNowForDebug(Find.CurrentMap, plan, out message, out traceText);
            AddLog("Execute: " + (fired ? "ok" : "failed") + " - " + message);
            if (!traceText.NullOrEmpty())
            {
                AddLog("Execute trace: " + traceText.Replace("\n", " | "));
            }
        }

        private static void FireTriggeredRaid(Dictionary<string, string> arguments)
        {
            StorytellerComp_DeepseekOrca comp = OrcaStorytellerUtility.ActiveOrcaComp();
            if (comp == null)
            {
                AddLog("Execute: failed - active storyteller does not contain StorytellerComp_DeepseekOrca");
                return;
            }

            string message;
            string traceText;
            bool fired = comp.TryFireRaidNowForDebug(Find.CurrentMap, arguments, out message, out traceText);
            AddLog("Execute: " + (fired ? "ok" : "failed") + " - " + message);
            if (!traceText.NullOrEmpty())
            {
                AddLog("Execute trace: " + traceText.Replace("\n", " | "));
            }
        }

        private static void SpawnPawns(Dictionary<string, string> arguments)
        {
            AiToolContext context = new AiToolContext(Find.CurrentMap, null, null);
            string message;
            bool spawned = OrcaPawnSpawnUtility.TrySpawnPawns(context, arguments, out message);
            AddLog("Execute: " + (spawned ? "ok" : "failed") + " - " + message);
        }

        private static string FormatArguments(Dictionary<string, string> arguments)
        {
            if (arguments == null || arguments.Count == 0)
            {
                return "{}";
            }

            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string> pair in arguments)
            {
                parts.Add(pair.Key + "=" + pair.Value);
            }

            return "{" + string.Join(", ", parts.ToArray()) + "}";
        }

        private static string FormatValues(AiToolResult result)
        {
            if (result.values == null || result.values.Count == 0)
            {
                return "";
            }

            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, string> pair in result.values)
            {
                parts.Add(pair.Key + "=" + pair.Value);
            }

            return " [" + string.Join(", ", parts.ToArray()) + "]";
        }
    }
}
