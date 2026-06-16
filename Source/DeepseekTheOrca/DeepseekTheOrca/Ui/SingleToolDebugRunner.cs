using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
    public static class SingleToolDebugRunner
    {
        private static readonly List<string> logLines = new List<string>();

        public static IEnumerable<string> LogLines
        {
            get { return logLines; }
        }

        public static void Run(string toolName, Dictionary<string, string> arguments)
        {
            if (Find.CurrentMap == null)
            {
                AddLog("No current map.");
                return;
            }

            arguments = arguments ?? new Dictionary<string, string>();

            AiToolContext context = new AiToolContext(Find.CurrentMap, null, null);
            AiToolSession session = new AiToolSession(context);

            AddLog("Run tool: " + toolName + " " + FormatArguments(arguments));
            AiToolResult result = session.Invoke(toolName, arguments);
            AddLog("Result: " + (result.success ? "ok" : "failed") + " - " + result.message + FormatValues(result));

            if (context.trace.Length > 0)
            {
                AddLog("Trace: " + context.trace.ToString().Replace("\n", " | "));
            }

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
