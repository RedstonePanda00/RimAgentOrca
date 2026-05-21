using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class GetColonySummaryTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "get_colony_summary"; }
        }

        public string Description
        {
            get { return "Read a compact storyteller-safe summary of the current incident target."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            ColonySnapshot snapshot = ColonySnapshot.Capture(context.target);
            return AiToolResult.Ok("colony summary captured")
                .WithValue("colonists", snapshot.colonists)
                .WithValue("downedColonists", snapshot.downedColonists)
                .WithValue("mentalStateColonists", snapshot.mentalStateColonists)
                .WithValue("averageMood", snapshot.averageMood.ToStringPercent())
                .WithValue("playerWealth", snapshot.playerWealth.ToString("F0"))
                .WithValue("threatPoints", snapshot.threatPoints.ToString("F0"))
                .WithValue("humanEdibleNutrition", snapshot.humanEdibleNutrition.ToString("F1"))
                .WithValue("recentIncidents", snapshot.recentIncidents);
        }
    }

    public sealed class ListAvailableIncidentsTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "list_available_incidents"; }
        }

        public string Description
        {
            get { return "List cached incidents that target the current map/world and can fire now."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            List<CachedIncidentDef> available = OrcaIncidentDefCache.AvailableFor(context).ToList();
            List<string> summaries = available.Select(incident => incident.Summary).ToList();

            return AiToolResult.Ok("available incident count: " + available.Count)
                .WithValue("incidentDefs", string.Join("; ", summaries.ToArray()));
        }
    }

    public sealed class GetRecentLettersTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "get_recent_letters"; }
        }

        public string Description
        {
            get { return "Read recent letters from the game archive, falling back to the visible LetterStack."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            int count = ParseCount(arguments);
            List<Letter> letters = RecentArchivedLetters(count);
            if (letters.Count == 0)
            {
                letters = RecentVisibleLetters(count);
            }

            if (letters.Count == 0)
            {
                return AiToolResult.Ok("no recent letters").WithValue("letters", "");
            }

            List<string> summaries = new List<string>();
            for (int i = letters.Count - 1; i >= 0; i--)
            {
                summaries.Add(FormatLetter(letters[i]));
            }

            return AiToolResult.Ok("recent letter count: " + summaries.Count)
                .WithValue("letters", string.Join(" || ", summaries.ToArray()));
        }

        private static int ParseCount(Dictionary<string, string> arguments)
        {
            int count = 5;
            string countText;
            if (arguments.TryGetValue("count", out countText))
            {
                int.TryParse(countText, out count);
            }
            return Mathf.Clamp(count <= 0 ? 5 : count, 1, 10);
        }

        private static List<Letter> RecentArchivedLetters(int count)
        {
            if (Find.Archive == null || Find.Archive.ArchivablesListForReading == null)
            {
                return new List<Letter>();
            }

            List<Letter> archivedLetters = Find.Archive.ArchivablesListForReading
                .OfType<Letter>()
                .OrderBy(letter => letter.arrivalTick)
                .ToList();

            if (archivedLetters.Count <= count)
            {
                return archivedLetters;
            }

            return archivedLetters.GetRange(archivedLetters.Count - count, count);
        }

        private static List<Letter> RecentVisibleLetters(int count)
        {
            if (Find.LetterStack == null || Find.LetterStack.LettersListForReading == null)
            {
                return new List<Letter>();
            }

            List<Letter> visibleLetters = Find.LetterStack.LettersListForReading;
            int start = Mathf.Max(0, visibleLetters.Count - count);
            return visibleLetters.GetRange(start, visibleLetters.Count - start);
        }

        private static string FormatLetter(Letter letter)
        {
            string label = letter.Label.Resolve();
            string defName = letter.def == null ? "" : letter.def.defName;
            string faction = letter.relatedFaction == null ? "" : letter.relatedFaction.Name;
            string title = "";
            string text = "";
            string questName = "";

            ChoiceLetter choiceLetter = letter as ChoiceLetter;
            if (choiceLetter != null)
            {
                if (!choiceLetter.title.NullOrEmpty())
                {
                    title = choiceLetter.title;
                }
                text = choiceLetter.Text.Resolve();
                if (choiceLetter.quest != null)
                {
                    questName = choiceLetter.quest.name;
                }
            }

            if (!text.NullOrEmpty() && text.Length > 500)
            {
                text = text.Substring(0, 500) + "...";
            }

            List<string> parts = new List<string>();
            parts.Add("label=" + label);
            if (!defName.NullOrEmpty())
            {
                parts.Add("def=" + defName);
            }
            if (!title.NullOrEmpty())
            {
                parts.Add("title=" + title);
            }
            parts.Add("arrivalTick=" + letter.arrivalTick);
            if (!faction.NullOrEmpty())
            {
                parts.Add("faction=" + faction);
            }
            if (!questName.NullOrEmpty())
            {
                parts.Add("quest=" + questName);
            }
            if (!text.NullOrEmpty())
            {
                parts.Add("text=" + text);
            }

            return "[" + string.Join(", ", parts.ToArray()) + "]";
        }
    }

    public sealed class ListMapPawnsTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "list_map_pawns"; }
        }

        public string Description
        {
            get { return "List spawned pawns on the current map and return pawnId values for follow-up detail queries."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            Map map = context == null ? null : context.Map;
            if (map == null || map.mapPawns == null)
            {
                return AiToolResult.Fail("no current map");
            }

            int count = ParseBoundedInt(arguments, "count", 50, 1, 100);
            string filter = "";
            arguments.TryGetValue("filter", out filter);
            filter = filter.NullOrEmpty() ? "all" : filter.Trim().ToLowerInvariant();

            List<Pawn> pawns = map.mapPawns.AllPawnsSpawned
                .Where(pawn => pawn != null && PawnMatchesFilter(pawn, filter))
                .OrderBy(PawnSortBucket)
                .ThenBy(pawn => pawn.LabelShort)
                .Take(count)
                .ToList();

            List<string> summaries = pawns.Select(PawnListSummary).ToList();
            return AiToolResult.Ok("map pawn count: " + pawns.Count)
                .WithValue("filter", filter)
                .WithValue("pawns", string.Join(" || ", summaries.ToArray()));
        }

        private static bool PawnMatchesFilter(Pawn pawn, string filter)
        {
            switch (filter)
            {
                case "player":
                case "colonist":
                case "colonists":
                    return pawn.IsColonist || pawn.IsColonyMech || pawn.IsColonyAnimal || pawn.IsSlaveOfColony || pawn.IsPrisonerOfColony;
                case "free_colonist":
                case "freecolonist":
                    return pawn.IsFreeColonist;
                case "humanlike":
                    return pawn.RaceProps != null && pawn.RaceProps.Humanlike;
                case "animal":
                case "animals":
                    return pawn.RaceProps != null && pawn.RaceProps.Animal;
                case "hostile":
                    return pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer);
                case "prisoner":
                case "prisoners":
                    return pawn.IsPrisonerOfColony;
                case "slave":
                case "slaves":
                    return pawn.IsSlaveOfColony;
                case "all":
                default:
                    return true;
            }
        }

        private static int PawnSortBucket(Pawn pawn)
        {
            if (pawn.IsFreeColonist)
            {
                return 0;
            }
            if (pawn.IsSlaveOfColony || pawn.IsPrisonerOfColony)
            {
                return 1;
            }
            if (pawn.IsColonyMech || pawn.IsColonyAnimal)
            {
                return 2;
            }
            if (pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer))
            {
                return 3;
            }
            return 4;
        }

        private static string PawnListSummary(Pawn pawn)
        {
            List<string> parts = new List<string>();
            parts.Add("pawnId=" + PawnToolUtility.PawnId(pawn));
            parts.Add("name=" + PawnToolUtility.PawnName(pawn));
            parts.Add("kind=" + pawn.KindLabel);
            parts.Add("type=" + PawnToolUtility.PawnType(pawn));
            parts.Add("faction=" + PawnToolUtility.FactionName(pawn.Faction));
            parts.Add("pos=" + pawn.Position);
            if (pawn.Downed)
            {
                parts.Add("downed=true");
            }
            if (pawn.InMentalState && pawn.MentalStateDef != null)
            {
                parts.Add("mentalState=" + pawn.MentalStateDef.defName);
            }
            return "[" + string.Join(", ", parts.ToArray()) + "]";
        }

        private static int ParseBoundedInt(Dictionary<string, string> arguments, string key, int defaultValue, int min, int max)
        {
            int value = defaultValue;
            string text;
            if (arguments != null && arguments.TryGetValue(key, out text))
            {
                int.TryParse(text, out value);
            }
            return Mathf.Clamp(value <= 0 ? defaultValue : value, min, max);
        }
    }

    public sealed class GetPawnDetailsTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "get_pawn_details"; }
        }

        public string Description
        {
            get { return "Read detailed information about one spawned pawn by pawnId or name."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            Map map = context == null ? null : context.Map;
            if (map == null || map.mapPawns == null)
            {
                return AiToolResult.Fail("no current map");
            }

            string pawnId;
            if (arguments == null || !arguments.TryGetValue("pawnId", out pawnId) || pawnId.NullOrEmpty())
            {
                return AiToolResult.Fail("missing argument: pawnId");
            }

            Pawn pawn = PawnToolUtility.FindPawn(map, pawnId);
            if (pawn == null)
            {
                return AiToolResult.Fail("pawn not found: " + pawnId);
            }

            return AiToolResult.Ok("pawn details captured")
                .WithValue("pawnId", PawnToolUtility.PawnId(pawn))
                .WithValue("name", PawnToolUtility.PawnName(pawn))
                .WithValue("kind", pawn.KindLabel)
                .WithValue("type", PawnToolUtility.PawnType(pawn))
                .WithValue("faction", PawnToolUtility.FactionName(pawn.Faction))
                .WithValue("position", pawn.Position)
                .WithValue("state", PawnState(pawn))
                .WithValue("needs", NeedsSummary(pawn))
                .WithValue("traits", TraitsSummary(pawn))
                .WithValue("skills", SkillsSummary(pawn))
                .WithValue("relations", RelationsSummary(map, pawn))
                .WithValue("health", HealthSummary(pawn));
        }

        private static string PawnState(Pawn pawn)
        {
            List<string> parts = new List<string>();
            parts.Add("dead=" + pawn.Dead);
            parts.Add("downed=" + pawn.Downed);
            if (pawn.InMentalState && pawn.MentalStateDef != null)
            {
                parts.Add("mentalState=" + pawn.MentalStateDef.defName);
            }
            else
            {
                parts.Add("mentalState=none");
            }
            if (pawn.CurJobDef != null)
            {
                parts.Add("currentJob=" + pawn.CurJobDef.defName);
            }
            return string.Join(", ", parts.ToArray());
        }

        private static string NeedsSummary(Pawn pawn)
        {
            if (pawn.needs == null)
            {
                return "";
            }

            List<string> parts = new List<string>();
            if (pawn.needs.mood != null)
            {
                parts.Add("mood=" + pawn.needs.mood.CurLevelPercentage.ToStringPercent());
            }
            if (pawn.needs.food != null)
            {
                parts.Add("food=" + pawn.needs.food.CurLevelPercentage.ToStringPercent());
            }
            if (pawn.needs.rest != null)
            {
                parts.Add("rest=" + pawn.needs.rest.CurLevelPercentage.ToStringPercent());
            }
            if (pawn.needs.joy != null)
            {
                parts.Add("recreation=" + pawn.needs.joy.CurLevelPercentage.ToStringPercent());
            }
            return string.Join(", ", parts.ToArray());
        }

        private static string TraitsSummary(Pawn pawn)
        {
            if (pawn.story == null || pawn.story.traits == null || pawn.story.traits.allTraits.NullOrEmpty())
            {
                return "";
            }

            List<string> parts = new List<string>();
            foreach (Trait trait in pawn.story.traits.allTraits)
            {
                if (trait == null || trait.def == null)
                {
                    continue;
                }

                string label = trait.LabelCap;
                if (trait.Suppressed)
                {
                    label += " (suppressed)";
                }
                parts.Add(label + "[" + trait.def.defName + ":" + trait.Degree + "]");
            }

            return string.Join("; ", parts.ToArray());
        }

        private static string SkillsSummary(Pawn pawn)
        {
            if (pawn.skills == null || pawn.skills.skills.NullOrEmpty())
            {
                return "";
            }

            List<string> parts = new List<string>();
            foreach (SkillRecord skill in pawn.skills.skills.OrderByDescending(skill => skill.Level))
            {
                if (skill == null || skill.def == null)
                {
                    continue;
                }

                string passion = skill.passion == Passion.None ? "" : "/" + skill.passion;
                string disabled = skill.TotallyDisabled ? "/disabled" : "";
                parts.Add(skill.def.defName + "=" + skill.Level + passion + disabled);
            }

            return string.Join("; ", parts.ToArray());
        }

        private static string RelationsSummary(Map map, Pawn pawn)
        {
            List<string> parts = new List<string>();
            if (pawn.relations != null && !pawn.relations.DirectRelations.NullOrEmpty())
            {
                foreach (DirectPawnRelation relation in pawn.relations.DirectRelations.Take(20))
                {
                    if (relation == null || relation.def == null || relation.otherPawn == null)
                    {
                        continue;
                    }

                    parts.Add(relation.def.label + "=" + PawnToolUtility.PawnName(relation.otherPawn) + "(" + PawnToolUtility.PawnId(relation.otherPawn) + ")");
                }
            }

            if (pawn.RaceProps != null && pawn.RaceProps.Humanlike && pawn.relations != null)
            {
                foreach (Pawn other in map.mapPawns.AllPawnsSpawned.Where(other => other != null && other != pawn && other.RaceProps != null && other.RaceProps.Humanlike).Take(80))
                {
                    int opinion = pawn.relations.OpinionOf(other);
                    if (opinion >= 20 || opinion <= -20)
                    {
                        parts.Add("opinionOf " + PawnToolUtility.PawnName(other) + "(" + PawnToolUtility.PawnId(other) + ")=" + opinion);
                    }
                    if (parts.Count >= 30)
                    {
                        break;
                    }
                }
            }

            return parts.Count == 0 ? "" : string.Join("; ", parts.ToArray());
        }

        private static string HealthSummary(Pawn pawn)
        {
            List<string> parts = new List<string>();
            if (pawn.health == null || pawn.health.hediffSet == null)
            {
                return "";
            }

            parts.Add("state=" + pawn.health.State);
            parts.Add("pain=" + pawn.health.hediffSet.PainTotal.ToStringPercent());
            parts.Add("bleedRate=" + pawn.health.hediffSet.BleedRateTotal.ToStringPercent());
            parts.Add("capacities=" + CapacitiesSummary(pawn));

            List<string> hediffs = new List<string>();
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs.Where(hediff => hediff != null).Take(25))
            {
                List<string> hediffParts = new List<string>();
                hediffParts.Add(hediff.LabelCap);
                if (hediff.Part != null)
                {
                    hediffParts.Add("part=" + hediff.Part.Label);
                }
                if (!hediff.SeverityLabel.NullOrEmpty())
                {
                    hediffParts.Add("severity=" + hediff.SeverityLabel);
                }
                if (hediff.Bleeding)
                {
                    hediffParts.Add("bleeding");
                }
                if (hediff.IsCurrentlyLifeThreatening)
                {
                    hediffParts.Add("lifeThreatening");
                }
                if (hediff.TendableNow())
                {
                    hediffParts.Add("tendable");
                }
                hediffs.Add("[" + string.Join(", ", hediffParts.ToArray()) + "]");
            }

            parts.Add("hediffs=" + (hediffs.Count == 0 ? "none" : string.Join("; ", hediffs.ToArray())));
            return string.Join(" | ", parts.ToArray());
        }

        private static string CapacitiesSummary(Pawn pawn)
        {
            if (pawn.health == null || pawn.health.capacities == null)
            {
                return "";
            }

            List<string> parts = new List<string>();
            AddCapacity(parts, pawn, PawnCapacityDefOf.Consciousness);
            AddCapacity(parts, pawn, PawnCapacityDefOf.Moving);
            AddCapacity(parts, pawn, PawnCapacityDefOf.Manipulation);
            AddCapacity(parts, pawn, PawnCapacityDefOf.Sight);
            AddCapacity(parts, pawn, PawnCapacityDefOf.Hearing);
            AddCapacity(parts, pawn, PawnCapacityDefOf.Talking);
            return string.Join(", ", parts.ToArray());
        }

        private static void AddCapacity(List<string> parts, Pawn pawn, PawnCapacityDef capacity)
        {
            if (pawn.health.capacities.CapableOf(capacity))
            {
                parts.Add(capacity.defName + "=" + pawn.health.capacities.GetLevel(capacity).ToStringPercent());
            }
            else
            {
                parts.Add(capacity.defName + "=incapable");
            }
        }
    }

    public static class PawnToolUtility
    {
        public static string PawnId(Pawn pawn)
        {
            return pawn == null ? "" : pawn.thingIDNumber.ToString();
        }

        public static string PawnName(Pawn pawn)
        {
            if (pawn == null)
            {
                return "";
            }

            return pawn.Name == null ? pawn.LabelShort : pawn.Name.ToStringFull;
        }

        public static string PawnType(Pawn pawn)
        {
            if (pawn == null)
            {
                return "";
            }
            if (pawn.IsFreeColonist)
            {
                return "freeColonist";
            }
            if (pawn.IsSlaveOfColony)
            {
                return "slaveOfColony";
            }
            if (pawn.IsPrisonerOfColony)
            {
                return "prisonerOfColony";
            }
            if (pawn.IsColonyMech)
            {
                return "colonyMech";
            }
            if (pawn.IsColonyAnimal)
            {
                return "colonyAnimal";
            }
            if (pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer))
            {
                return "hostile";
            }
            return "other";
        }

        public static string FactionName(Faction faction)
        {
            if (faction == null)
            {
                return "none";
            }

            return faction.Name.NullOrEmpty() ? faction.def.defName : faction.Name;
        }

        public static Pawn FindPawn(Map map, string pawnIdOrName)
        {
            if (map == null || map.mapPawns == null || pawnIdOrName.NullOrEmpty())
            {
                return null;
            }

            string needle = pawnIdOrName.Trim();
            int id;
            if (int.TryParse(needle, out id))
            {
                Pawn byNumber = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn != null && pawn.thingIDNumber == id);
                if (byNumber != null)
                {
                    return byNumber;
                }
            }

            Pawn exact = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn != null && MatchesPawnText(pawn, needle, exactMatch: true));
            if (exact != null)
            {
                return exact;
            }

            return map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn != null && MatchesPawnText(pawn, needle, exactMatch: false));
        }

        private static bool MatchesPawnText(Pawn pawn, string needle, bool exactMatch)
        {
            List<string> values = new List<string>
            {
                pawn.ThingID,
                pawn.GetUniqueLoadID(),
                pawn.LabelShort,
                pawn.LabelNoCount,
                PawnName(pawn)
            };

            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                if (value.NullOrEmpty())
                {
                    continue;
                }

                if (exactMatch)
                {
                    if (string.Equals(value, needle, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                else if (value.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class CanFireIncidentTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "can_fire_incident"; }
        }

        public string Description
        {
            get { return "Validate one cached incident def against the current target and storyteller settings."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            string defName;
            if (!arguments.TryGetValue("incidentDef", out defName) || defName.NullOrEmpty())
            {
                return AiToolResult.Fail("missing argument: incidentDef");
            }

            float pointsFactor = 1f;
            string pointsFactorText;
            if (arguments.TryGetValue("pointsFactor", out pointsFactorText))
            {
                float.TryParse(pointsFactorText, out pointsFactor);
            }

            AiIncidentPlan plan = AiIncidentPlan.For(defName, "validation only", pointsFactor);
            FiringIncident ignored;
            string reason;
            if (OrcaIncidentValidator.TryBuildFiringIncident(plan, context, out ignored, out reason))
            {
                return AiToolResult.Ok(defName + " can fire");
            }

            return AiToolResult.Fail(reason);
        }
    }

    public sealed class ProposeIncidentTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "propose_incident"; }
        }

        public string Description
        {
            get { return "Create a structured incident proposal. This does not execute anything."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            string defName;
            if (!arguments.TryGetValue("incidentDef", out defName) || defName.NullOrEmpty())
            {
                return AiToolResult.Fail("missing argument: incidentDef");
            }

            string reason;
            arguments.TryGetValue("reason", out reason);

            float pointsFactor = 1f;
            string pointsFactorText;
            if (arguments.TryGetValue("pointsFactor", out pointsFactorText))
            {
                float.TryParse(pointsFactorText, out pointsFactor);
            }

            return AiToolResult.Ok("incident proposal created")
                .WithValue("incidentDef", defName)
                .WithValue("pointsFactor", pointsFactor.ToString("F2"))
                .WithValue("reason", reason ?? "");
        }
    }

    public sealed class ScheduleIncidentTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "schedule_incident"; }
        }

        public string Description
        {
            get { return "Validate an incident proposal for storyteller execution. The comp still owns the actual firing."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            string defName;
            if (!arguments.TryGetValue("incidentDef", out defName) || defName.NullOrEmpty())
            {
                return AiToolResult.Fail("missing argument: incidentDef");
            }

            float pointsFactor = 1f;
            string pointsFactorText;
            if (arguments.TryGetValue("pointsFactor", out pointsFactorText))
            {
                float.TryParse(pointsFactorText, out pointsFactor);
            }

            string reason;
            arguments.TryGetValue("reason", out reason);

            AiIncidentPlan plan = AiIncidentPlan.For(defName, reason ?? "AI storyteller selected this incident.", pointsFactor);
            FiringIncident ignored;
            string rejectReason;
            if (!OrcaIncidentValidator.TryBuildFiringIncident(plan, context, out ignored, out rejectReason))
            {
                return AiToolResult.Fail(rejectReason);
            }

            return AiToolResult.Ok("incident validated for scheduling")
                .WithValue("incidentDef", defName)
                .WithValue("pointsFactor", pointsFactor.ToString("F2"))
                .WithValue("reason", reason ?? "");
        }
    }

    public sealed class TriggerRaidTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "trigger_raid"; }
        }

        public string Description
        {
            get { return "Validate a precise enemy raid request with optional faction, raid strategy, arrival mode, and spawn cell. Execution is owned by the caller."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            IncidentParms parms;
            string rejectReason;
            if (!OrcaRaidUtility.TryBuildRaidParms(context, arguments, out parms, out rejectReason))
            {
                return AiToolResult.Fail(rejectReason);
            }

            return AiToolResult.Ok("raid validated for triggering")
                .WithValue("incidentDef", "RaidEnemy")
                .WithValue("faction", parms.faction == null ? "" : parms.faction.def.defName)
                .WithValue("raidStrategy", parms.raidStrategy == null ? "" : parms.raidStrategy.defName)
                .WithValue("raidArrivalMode", parms.raidArrivalMode == null ? "" : parms.raidArrivalMode.defName)
                .WithValue("spawnCenter", parms.spawnCenter.IsValid ? parms.spawnCenter.ToString() : "")
                .WithValue("points", parms.points.ToString("F0"));
        }
    }

    public sealed class SpawnPawnsTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "spawn_pawns"; }
        }

        public string Description
        {
            get { return "Validate spawning a number of default faction pawns near a specified map cell. Execution is owned by the caller."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            PawnSpawnRequest request;
            string rejectReason;
            if (!OrcaPawnSpawnUtility.TryBuildRequest(context, arguments, out request, out rejectReason))
            {
                return AiToolResult.Fail(rejectReason);
            }

            return AiToolResult.Ok("pawn spawn validated")
                .WithValue("faction", request.faction.def.defName)
                .WithValue("count", request.count)
                .WithValue("spawnCell", request.spawnCell)
                .WithValue("radius", request.radius);
        }
    }
}

