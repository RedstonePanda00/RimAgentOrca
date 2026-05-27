using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaRaidUtility
    {
        private const string RaidIncidentDefName = "RaidEnemy";
        private const string SpecificDropArrivalModeDefName = "SpecificDropDebug";

        public static bool TryBuildRaidParms(AiToolContext context, Dictionary<string, string> arguments, out IncidentParms parms, out string rejectReason)
        {
            parms = null;
            rejectReason = null;

            Map map = context == null ? null : context.Map;
            if (map == null)
            {
                rejectReason = "no current map";
                return false;
            }

            IncidentDef raidDef = IncidentDefOf.RaidEnemy;
            parms = StorytellerUtility.DefaultParmsNow(raidDef.category, map);
            if (parms == null)
            {
                rejectReason = "could not create raid incident parms";
                return false;
            }

            parms.target = map;
            parms.forced = true;
            parms.pawnGroupKind = PawnGroupKindDefOf.Combat;

            float pointsFactor = ParseFloat(arguments, "pointsFactor", 1f);
            if (parms.points <= 0f)
            {
                parms.points = StorytellerUtility.DefaultThreatPointsNow(map);
            }
            parms.points *= Mathf.Clamp(pointsFactor <= 0f ? 1f : pointsFactor, 0.1f, 10f);

            string factionText = GetArg(arguments, "factionDef");
            string strategyText = GetArg(arguments, "raidStrategyDef");
            string spawnCellText = GetArg(arguments, "spawnCell");
            bool hasSpawnCell = !spawnCellText.NullOrEmpty();
            string arrivalText = GetArg(arguments, "raidArrivalModeDef");
            if (hasSpawnCell)
            {
                if (arrivalText.NullOrEmpty())
                {
                    arrivalText = SpecificDropArrivalModeDefName;
                }
                if (strategyText.NullOrEmpty())
                {
                    strategyText = RaidStrategyDefOf.ImmediateAttack.defName;
                }
            }

            if (factionText.NullOrEmpty() && (!strategyText.NullOrEmpty() || !arrivalText.NullOrEmpty()))
            {
                rejectReason = "factionDef is required when specifying raidStrategyDef, raidArrivalModeDef, or spawnCell";
                return false;
            }

            if (!factionText.NullOrEmpty())
            {
                Faction faction = FindFaction(factionText);
                if (faction == null)
                {
                    rejectReason = "faction not found: " + factionText;
                    return false;
                }

                parms.faction = faction;
            }

            if (!strategyText.NullOrEmpty())
            {
                RaidStrategyDef strategy = DefDatabase<RaidStrategyDef>.GetNamedSilentFail(strategyText);
                if (strategy == null)
                {
                    rejectReason = "raid strategy not found: " + strategyText;
                    return false;
                }

                parms.raidStrategy = strategy;
            }

            if (!arrivalText.NullOrEmpty())
            {
                PawnsArrivalModeDef arrivalMode = DefDatabase<PawnsArrivalModeDef>.GetNamedSilentFail(arrivalText);
                if (arrivalMode == null)
                {
                    rejectReason = "raid arrival mode not found: " + arrivalText;
                    return false;
                }

                parms.raidArrivalMode = arrivalMode;
            }

            if (hasSpawnCell)
            {
                IntVec3 cell;
                if (!TryParseCell(spawnCellText, out cell))
                {
                    rejectReason = "invalid spawnCell. Use x,z or x,y,z.";
                    return false;
                }
                if (!cell.InBounds(map))
                {
                    rejectReason = "spawnCell is outside the current map: " + cell;
                    return false;
                }
                if (parms.raidArrivalMode == null || !(parms.raidArrivalMode.Worker is PawnsArrivalModeWorker_SpecificLocationDrop))
                {
                    rejectReason = "spawnCell requires raidArrivalModeDef=" + SpecificDropArrivalModeDefName;
                    return false;
                }

                parms.spawnCenter = cell;
            }

            return ValidateRaidParms(parms, out rejectReason);
        }

        public static bool TryFireRaid(AiToolContext context, Dictionary<string, string> arguments, out string message, out string traceText)
        {
            message = "";
            traceText = "";

            IncidentParms parms;
            string rejectReason;
            if (!TryBuildRaidParms(context, arguments, out parms, out rejectReason))
            {
                message = rejectReason;
                traceText = context == null ? "" : context.trace.ToString();
                return false;
            }

            FiringIncident firingIncident = new FiringIncident(IncidentDefOf.RaidEnemy, context.source, parms);
            if (Find.Storyteller == null || !Find.Storyteller.TryFire(firingIncident))
            {
                message = "Storyteller.TryFire returned false for RaidEnemy";
                traceText = context.trace.ToString();
                return false;
            }

            message = "raid fired: " + (parms.faction == null ? "unknown faction" : parms.faction.def.defName)
                + ", " + (parms.raidStrategy == null ? "unknown strategy" : parms.raidStrategy.defName)
                + ", " + (parms.raidArrivalMode == null ? "unknown arrival" : parms.raidArrivalMode.defName);
            if (parms.spawnCenter.IsValid)
            {
                message += ", spawnCenter=" + parms.spawnCenter;
            }

            traceText = context.trace.ToString();
            return true;
        }

        private static bool ValidateRaidParms(IncidentParms parms, out string rejectReason)
        {
            rejectReason = null;
            IncidentWorker_RaidEnemy worker = IncidentDefOf.RaidEnemy.Worker as IncidentWorker_RaidEnemy;
            if (worker == null)
            {
                rejectReason = "RaidEnemy worker is unavailable";
                return false;
            }

            if (parms.faction != null && !worker.FactionCanBeGroupSource(parms.faction, parms))
            {
                rejectReason = "faction cannot be used as a raid source: " + parms.faction.def.defName;
                return false;
            }

            if (parms.raidStrategy != null && !parms.raidStrategy.Worker.CanUseWith(parms, PawnGroupKindDefOf.Combat))
            {
                rejectReason = "raid strategy cannot be used with these parms: " + parms.raidStrategy.defName;
                return false;
            }

            if (parms.raidArrivalMode != null)
            {
                if (parms.raidStrategy != null && (parms.raidStrategy.arriveModes == null || !parms.raidStrategy.arriveModes.Contains(parms.raidArrivalMode)))
                {
                    rejectReason = "raid arrival mode is not allowed by strategy: " + parms.raidArrivalMode.defName;
                    return false;
                }

                if (!parms.raidArrivalMode.Worker.CanUseWith(parms))
                {
                    rejectReason = "raid arrival mode cannot be used with these parms: " + parms.raidArrivalMode.defName;
                    return false;
                }
            }

            if (!IncidentDefOf.RaidEnemy.Worker.CanFireNow(parms))
            {
                rejectReason = "RaidEnemy.CanFireNow returned false";
                return false;
            }

            return true;
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
                && (string.Equals(faction.def.defName, needle, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(faction.Name, needle, System.StringComparison.OrdinalIgnoreCase)
                    || (!faction.Name.NullOrEmpty() && faction.Name.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0)));
        }

        private static bool TryParseCell(string text, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (text.NullOrEmpty())
            {
                return false;
            }

            string cleaned = text.Replace("(", "").Replace(")", "");
            string[] parts = cleaned.Split(',');
            if (parts.Length != 2 && parts.Length != 3)
            {
                return false;
            }

            int x;
            int z;
            if (!int.TryParse(parts[0].Trim(), out x))
            {
                return false;
            }

            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[1].Trim(), out z))
                {
                    return false;
                }
            }
            else if (!int.TryParse(parts[2].Trim(), out z))
            {
                return false;
            }

            cell = new IntVec3(x, 0, z);
            return true;
        }

        private static string GetArg(Dictionary<string, string> arguments, string key)
        {
            string value;
            return arguments != null && arguments.TryGetValue(key, out value) ? value : "";
        }

        private static float ParseFloat(Dictionary<string, string> arguments, string key, float defaultValue)
        {
            string text = GetArg(arguments, key);
            float value;
            return !text.NullOrEmpty() && float.TryParse(text, out value) ? value : defaultValue;
        }
    }
}
