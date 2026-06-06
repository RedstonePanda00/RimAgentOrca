using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class SearchMapPawnsTool : OrcaToolWorker
    {
        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            Map map = context == null ? null : context.Map;
            if (map == null || map.mapPawns == null)
            {
                return AiToolResult.Fail("no current map");
            }

            string query = GetArgument(arguments, "query");
            int count = ParseBoundedInt(arguments, "count", 5, 1, 20);
            string filter = GetArgument(arguments, "filter");
            filter = filter.NullOrEmpty() ? "all" : filter.Trim().ToLowerInvariant();

            List<ScoredPawn> pawns = map.mapPawns.AllPawnsSpawned
                .Where(pawn => pawn != null && PawnMatchesFilter(pawn, filter))
                .Select(pawn => new ScoredPawn(pawn, ScorePawn(pawn, query), RelevanceReason(pawn, query)))
                .OrderByDescending(item => item.score)
                .ThenBy(item => PawnSortBucket(item.pawn))
                .ThenBy(item => item.pawn.LabelShort)
                .Take(count)
                .ToList();

            List<string> summaries = pawns.Select(item => PawnListSummary(item.pawn, item.reason)).ToList();
            return AiToolResult.Ok("map pawn search result count: " + pawns.Count)
                .WithValue("query", query ?? "")
                .WithValue("filter", filter)
                .WithValue("pawns", string.Join(" || ", summaries.ToArray()));
        }

        private static float ScorePawn(Pawn pawn, string query)
        {
            float score = 0f;
            string lower = (query ?? "").ToLowerInvariant();
            if (!lower.NullOrEmpty())
            {
                AddIfContains(ref score, lower, PawnToolUtility.PawnName(pawn), 4f);
                AddIfContains(ref score, lower, pawn.LabelShort, 3f);
                AddIfContains(ref score, lower, pawn.KindLabel, 1f);
                AddIfContains(ref score, lower, PawnToolUtility.PawnType(pawn), 1f);
                AddIfContains(ref score, lower, PawnToolUtility.FactionName(pawn.Faction), 1f);
            }

            if (ContainsAny(lower, "受伤", "疼", "流血", "健康", "injury", "health", "pain", "bleed") && pawn.health != null && pawn.health.hediffSet != null)
            {
                score += pawn.health.hediffSet.hediffs.Any(hediff => hediff != null) ? 2.5f : 0f;
                score += pawn.health.hediffSet.BleedRateTotal > 0f ? 2f : 0f;
                score += pawn.health.hediffSet.PainTotal > 0.05f ? 1.5f : 0f;
            }
            if (ContainsAny(lower, "心情", "崩溃", "mood", "mental") && pawn.needs != null && pawn.needs.mood != null)
            {
                score += 1f - pawn.needs.mood.CurLevelPercentage;
                score += pawn.InMentalState ? 2f : 0f;
            }
            if (ContainsAny(lower, "关系", "恋人", "朋友", "敌人", "家人", "走到一起", "社交", "relation", "relationship", "lover", "friend", "romance") && pawn.RaceProps != null && pawn.RaceProps.Humanlike)
            {
                score += 2f;
            }
            if (ContainsAny(lower, "技能", "工作", "skill", "job") && pawn.skills != null)
            {
                score += 1.5f;
            }
            if (ContainsAny(lower, "敌人", "袭击", "hostile", "enemy", "raid") && pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer))
            {
                score += 2.5f;
            }
            if (ContainsAny(lower, "殖民者", "小人", "colonist", "pawn") && pawn.IsFreeColonist)
            {
                score += 1.5f;
            }

            return score + DefaultPawnWeight(pawn);
        }

        private static string RelevanceReason(Pawn pawn, string query)
        {
            string lower = (query ?? "").ToLowerInvariant();
            List<string> reasons = new List<string>();
            if (pawn.Downed)
            {
                reasons.Add("downed");
            }
            if (pawn.InMentalState && pawn.MentalStateDef != null)
            {
                reasons.Add("mentalState=" + pawn.MentalStateDef.defName);
            }
            if (pawn.health != null && pawn.health.hediffSet != null && pawn.health.hediffSet.BleedRateTotal > 0f)
            {
                reasons.Add("bleeding");
            }
            if (ContainsAny(lower, "关系", "恋人", "走到一起", "relationship", "romance") && pawn.RaceProps != null && pawn.RaceProps.Humanlike)
            {
                reasons.Add("social candidate");
            }
            if (reasons.Count == 0)
            {
                reasons.Add(PawnToolUtility.PawnType(pawn));
            }
            return string.Join("; ", reasons.ToArray());
        }

        private static float DefaultPawnWeight(Pawn pawn)
        {
            if (pawn.IsFreeColonist) return 1.2f;
            if (pawn.IsSlaveOfColony || pawn.IsPrisonerOfColony) return 0.8f;
            if (pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer)) return 0.7f;
            return 0.2f;
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
            if (pawn.IsFreeColonist) return 0;
            if (pawn.IsSlaveOfColony || pawn.IsPrisonerOfColony) return 1;
            if (pawn.IsColonyMech || pawn.IsColonyAnimal) return 2;
            if (pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer)) return 3;
            return 4;
        }

        private static string PawnListSummary(Pawn pawn, string reason)
        {
            List<string> parts = new List<string>();
            parts.Add("pawnId=" + PawnToolUtility.PawnId(pawn));
            parts.Add("name=" + PawnToolUtility.PawnName(pawn));
            parts.Add("kind=" + pawn.KindLabel);
            parts.Add("type=" + PawnToolUtility.PawnType(pawn));
            parts.Add("faction=" + PawnToolUtility.FactionName(pawn.Faction));
            parts.Add("keyState=" + PawnDetailsFormatter.PawnState(pawn));
            parts.Add("relevanceReason=" + reason);
            return "[" + string.Join(", ", parts.ToArray()) + "]";
        }

        private static void AddIfContains(ref float score, string lowerQuery, string value, float weight)
        {
            if (!value.NullOrEmpty() && lowerQuery.Contains(value.ToLowerInvariant()))
            {
                score += weight;
            }
        }

        private static string GetArgument(Dictionary<string, string> arguments, string key)
        {
            string value;
            return arguments != null && arguments.TryGetValue(key, out value) ? value : "";
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

        private static bool ContainsAny(string text, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (text.Contains(needles[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private sealed class ScoredPawn
        {
            public readonly Pawn pawn;
            public readonly float score;
            public readonly string reason;

            public ScoredPawn(Pawn pawn, float score, string reason)
            {
                this.pawn = pawn;
                this.score = score;
                this.reason = reason;
            }
        }
    }
}
