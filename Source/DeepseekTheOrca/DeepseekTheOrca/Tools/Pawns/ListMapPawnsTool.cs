using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
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
            parts.Add("race=" + PawnToolUtility.RaceSummary(pawn));
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
}
