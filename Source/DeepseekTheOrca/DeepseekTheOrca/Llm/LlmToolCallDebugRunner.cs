using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace DeepseekTheOrca
{
    public static class LlmToolCallDebugRunner
    {
        private static readonly LlmIncidentDecisionProvider provider = new LlmIncidentDecisionProvider();
        private static bool running;
        private static string status = "DTO_DebugToolCallNotRun";
        private static AiIncidentPlan lastPlan;

        public static bool IsRunning
        {
            get { return running; }
        }

        public static IEnumerable<string> LogLines
        {
            get { return OwnLogLines.Concat(provider.LogLines); }
        }

        private static readonly List<string> OwnLogLines = new List<string>();

        public static void Start()
        {
            if (running)
            {
                return;
            }

            if (Find.CurrentMap == null)
            {
                SetStatus("No current map.");
                return;
            }

            if (DeepseekTheOrcaMod.Settings == null || !DeepseekTheOrcaMod.Settings.HasConfiguredLlm)
            {
                SetStatus("LLM incident planning is not enabled or API key is empty.");
                return;
            }

            running = true;
            lastPlan = null;
            OwnLogLines.Clear();
            SetStatus("DTO_DebugToolCallRunning");
            AddLog("Manual tool-call test started.");
        }

        public static void Tick()
        {
            if (!running)
            {
                return;
            }

            if (Find.CurrentMap == null)
            {
                running = false;
                SetStatus("No current map.");
                return;
            }

            AiToolContext context = new AiToolContext(Find.CurrentMap, null, null);
            AiIncidentPlan plan = provider.SelectIncidentPlan(context);
            if (plan != null)
            {
                running = false;
                lastPlan = plan;
                SetStatus("Selected " + plan.incidentDefName + ": " + plan.reason);
                LogDebug("Debug tool-call test selected " + plan.incidentDefName + ". Reason: " + plan.reason + "\n" + context.trace);
                return;
            }

            SetStatus(provider.LastStatus.NullOrEmpty() ? "DTO_DebugToolCallRunning" : provider.LastStatus);
            if (!provider.HasPendingWork && status != "DTO_DebugToolCallRunning")
            {
                running = false;
            }
        }

        public static string StatusText()
        {
            if (status == "DTO_DebugToolCallNotRun" || status == "DTO_DebugToolCallRunning")
            {
                return status.Translate();
            }

            return status;
        }

        public static void ClearLog()
        {
            OwnLogLines.Clear();
            provider.ClearLog();
        }

        private static void SetStatus(string newStatus)
        {
            if (status == newStatus)
            {
                return;
            }

            status = newStatus;
            AddLog("Status: " + StatusText());
        }

        private static void AddLog(string line)
        {
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            OwnLogLines.Add("tick " + tick + " | " + line);
            while (OwnLogLines.Count > 100)
            {
                OwnLogLines.RemoveAt(0);
            }
        }

        private static void LogDebug(string message)
        {
            if (DeepseekTheOrcaMod.Settings != null && DeepseekTheOrcaMod.Settings.debugLogging)
            {
                Log.Message("[RimAgent] " + message);
            }
        }
    }
}
