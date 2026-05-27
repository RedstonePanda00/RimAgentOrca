using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;
namespace DeepseekTheOrca
{
    public sealed partial class OrcaChatSession
    {
        private AiToolResult InvokeScheduleIncidentFromChat(AiToolSession session, Dictionary<string, string> arguments)
        {
            AiToolResult validationResult = session.Invoke("schedule_incident", arguments);
            if (!validationResult.success)
            {
                return validationResult;
            }

            float chance = IncidentWillingnessChance(arguments);
            AddProcess("Willingness roll chance: " + chance.ToStringPercent());
            if (!Rand.Chance(chance))
            {
                AddProcess("Willingness roll failed.");
                return AiToolResult.Fail("Orca was unwilling to actually fire the incident.");
            }
            AddProcess("Willingness roll passed.");

            StorytellerComp_DeepseekOrca comp = ActiveOrcaComp();
            if (comp == null)
            {
                return AiToolResult.Fail("active storyteller does not contain StorytellerComp_DeepseekOrca");
            }

            AiIncidentPlan plan;
            string rejectReason;
            if (!TryBuildPlan(arguments, out plan, out rejectReason))
            {
                return AiToolResult.Fail(rejectReason);
            }

            string message;
            string traceText;
            bool fired = comp.TryFireIncidentNowForDebug(Find.CurrentMap, plan, out message, out traceText);
            if (!fired)
            {
                return AiToolResult.Fail(message);
            }

            return AiToolResult.Ok(message)
                .WithValue("incidentDef", plan.incidentDefName)
                .WithValue("reason", plan.reason ?? "");
        }

        private static string SanitizeVisibleReply(string text)
        {
            if (text.NullOrEmpty())
            {
                return text ?? "";
            }

            return OrcaVisibleReplySanitizer.Sanitize(text, trim: true);
        }

        private AiToolResult InvokeTriggerRaidFromChat(AiToolSession session, Dictionary<string, string> arguments)
        {
            AiToolResult validationResult = session.Invoke("trigger_raid", arguments);
            if (!validationResult.success)
            {
                return validationResult;
            }

            float chance = AggressiveWillingnessChance();
            AddProcess("Willingness roll chance: " + chance.ToStringPercent());
            if (!Rand.Chance(chance))
            {
                AddProcess("Willingness roll failed.");
                return AiToolResult.Fail("Orca was unwilling to actually fire the raid.");
            }
            AddProcess("Willingness roll passed.");

            StorytellerComp_DeepseekOrca comp = ActiveOrcaComp();
            if (comp == null)
            {
                return AiToolResult.Fail("active storyteller does not contain StorytellerComp_DeepseekOrca");
            }

            string message;
            string traceText;
            bool fired = comp.TryFireRaidNowForDebug(Find.CurrentMap, arguments, out message, out traceText);
            if (!traceText.NullOrEmpty())
            {
                AddProcess("Trigger raid trace: " + traceText.Replace("\n", " | "));
            }
            if (!fired)
            {
                return AiToolResult.Fail(message);
            }

            return AiToolResult.Ok(message);
        }

        private AiToolResult InvokeSpawnPawnsFromChat(AiToolSession session, Dictionary<string, string> arguments)
        {
            AiToolResult validationResult = session.Invoke("spawn_pawns", arguments);
            if (!validationResult.success)
            {
                return validationResult;
            }

            float chance = SpawnPawnsWillingnessChance(arguments);
            AddProcess("Willingness roll chance: " + chance.ToStringPercent());
            if (!Rand.Chance(chance))
            {
                AddProcess("Willingness roll failed.");
                return AiToolResult.Fail("Orca was unwilling to actually spawn pawns.");
            }
            AddProcess("Willingness roll passed.");

            AiToolContext context = new AiToolContext(Find.CurrentMap, null, null);
            string message;
            bool spawned = OrcaPawnSpawnUtility.TrySpawnPawns(context, arguments, out message);
            if (!spawned)
            {
                return AiToolResult.Fail(message);
            }

            return AiToolResult.Ok(message);
        }

        private static bool IsExecutionTool(string toolName)
        {
            return AiStoryToolRegistry.IsExecutionTool(toolName);
        }

        private static bool IsToolExposedToChat(string toolName)
        {
            if (toolName == "web_search" && (DeepseekTheOrcaMod.Settings == null || !DeepseekTheOrcaMod.Settings.UsesLocalWebSearchTool))
            {
                return false;
            }

            return AiStoryToolRegistry.IsExposedToChat(toolName) || OrcaHttpMcpClient.IsExposedTool(toolName);
        }

        private static bool ToolAllowsDuringProactive(string toolName)
        {
            return OrcaHttpMcpClient.IsExposedTool(toolName) || AiStoryToolRegistry.AllowsDuringProactive(toolName);
        }

        private static bool ToolRequiresCurrentMap(string toolName)
        {
            return !OrcaHttpMcpClient.IsExposedTool(toolName) && AiStoryToolRegistry.RequiresCurrentMap(toolName);
        }

        private float HelpfulWillingnessChance()
        {
            if (!OrcaMoodPlugin.Enabled)
            {
                return 1f;
            }

            return Mathf.Clamp(mood, 0, 100) / 100f;
        }

        private float AggressiveWillingnessChance()
        {
            float helpful = HelpfulWillingnessChance();
            if (mood <= 9)
            {
                return Mathf.Max(helpful, 1f - helpful);
            }

            return helpful;
        }

        private float IncidentWillingnessChance(Dictionary<string, string> arguments)
        {
            string incidentDef = GetArgument(arguments, "incidentDef");
            return IsPunitiveIncidentDef(incidentDef) ? AggressiveWillingnessChance() : HelpfulWillingnessChance();
        }

        private float SpawnPawnsWillingnessChance(Dictionary<string, string> arguments)
        {
            return IsHostileFactionArgument(arguments) ? AggressiveWillingnessChance() : HelpfulWillingnessChance();
        }

        private static bool IsPunitiveIncidentDef(string incidentDef)
        {
            if (incidentDef.NullOrEmpty())
            {
                return false;
            }

            string text = incidentDef.ToLowerInvariant();
            return text.Contains("raid")
                || text.Contains("manhunter")
                || text.Contains("infestation")
                || text.Contains("mech")
                || text.Contains("shipchunk")
                || text.Contains("shippart")
                || text.Contains("defoliator")
                || text.Contains("psychic")
                || text.Contains("toxic")
                || text.Contains("plague")
                || text.Contains("disease")
                || text.Contains("mad")
                || text.Contains("insanity")
                || text.Contains("volcanic")
                || text.Contains("cold")
                || text.Contains("heat")
                || text.Contains("eclipse");
        }

        private static bool IsHostileFactionArgument(Dictionary<string, string> arguments)
        {
            Faction faction = FindFaction(GetArgument(arguments, "factionDef"));
            return faction != null && Faction.OfPlayer != null && faction.HostileTo(Faction.OfPlayer);
        }

        private static Faction FindFaction(string factionText)
        {
            if (Find.FactionManager == null || factionText.NullOrEmpty())
            {
                return null;
            }

            string needle = factionText.Trim();
            FactionDef factionDef = DefDatabase<FactionDef>.GetNamedSilentFail(needle);
            if (factionDef != null)
            {
                Faction byDef = Find.FactionManager.FirstFactionOfDef(factionDef);
                if (byDef != null)
                {
                    return byDef;
                }
            }

            return Find.FactionManager.AllFactionsListForReading.FirstOrDefault(faction =>
                faction != null
                && (string.Equals(faction.def.defName, needle, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(faction.Name, needle, StringComparison.OrdinalIgnoreCase)
                    || (!faction.Name.NullOrEmpty() && faction.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)));
        }

        private static string GetArgument(Dictionary<string, string> arguments, string key)
        {
            string value;
            return arguments != null && arguments.TryGetValue(key, out value) ? value : "";
        }

        private static StorytellerComp_DeepseekOrca ActiveOrcaComp()
        {
            if (Find.Storyteller == null || Find.Storyteller.storytellerComps == null)
            {
                return null;
            }

            for (int i = 0; i < Find.Storyteller.storytellerComps.Count; i++)
            {
                StorytellerComp_DeepseekOrca comp = Find.Storyteller.storytellerComps[i] as StorytellerComp_DeepseekOrca;
                if (comp != null)
                {
                    return comp;
                }
            }

            return null;
        }

        private static bool TryBuildPlan(Dictionary<string, string> arguments, out AiIncidentPlan plan, out string rejectReason)
        {
            plan = null;
            rejectReason = null;

            string incidentDef;
            if (arguments == null || !arguments.TryGetValue("incidentDef", out incidentDef) || incidentDef.NullOrEmpty())
            {
                rejectReason = "missing argument: incidentDef";
                return false;
            }

            float pointsFactor = 1f;
            string pointsFactorText;
            if (arguments.TryGetValue("pointsFactor", out pointsFactorText))
            {
                float.TryParse(pointsFactorText, out pointsFactor);
            }

            string reason;
            arguments.TryGetValue("reason", out reason);
            plan = AiIncidentPlan.For(incidentDef, reason ?? "Orca chat selected this incident.", pointsFactor);
            return true;
        }

    }
}
