using System.Collections.Generic;
using System.Linq;
using DeepseekTheOrca.Rimtalk;
using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
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
                .WithValue("race", PawnToolUtility.RaceSummary(pawn))
                .WithValue("raceDescription", PawnToolUtility.RaceDescription(pawn))
                .WithValue("type", PawnToolUtility.PawnType(pawn))
                .WithValue("faction", PawnToolUtility.FactionName(pawn.Faction))
                .WithValue("position", pawn.Position)
                .WithValue("state", PawnState(pawn))
                .WithValue("needs", NeedsSummary(pawn))
                .WithValue("traits", TraitsSummary(pawn))
                .WithValue("skills", SkillsSummary(pawn))
                .WithValue("relations", RelationsSummary(map, pawn))
                .WithValue("health", HealthSummary(pawn))
                .WithValue("rimtalkPersona", RimtalkPersonaSummary(pawn));
        }

        private static string RimtalkPersonaSummary(Pawn pawn)
        {
            if (!RimtalkIntegration.IsAvailable)
            {
                return "";
            }

            string personality;
            float talkInitiationWeight;
            bool hasVocalLink;
            if (!RimtalkIntegration.TryGetPawnPersona(pawn, out personality, out talkInitiationWeight, out hasVocalLink))
            {
                return "";
            }

            List<string> parts = new List<string>();
            if (!personality.NullOrEmpty())
            {
                parts.Add("personality=" + personality);
            }

            if (talkInitiationWeight > 0f)
            {
                parts.Add("talkInitiationWeight=" + talkInitiationWeight.ToString("0.##"));
            }

            if (pawn.RaceProps != null && !pawn.RaceProps.Humanlike)
            {
                parts.Add("hasVocalLinkImplant=" + hasVocalLink);
            }

            return string.Join("; ", parts.ToArray());
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
}
