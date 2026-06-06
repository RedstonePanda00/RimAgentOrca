using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class ColonyChainSnapshot
    {
        public string state = "healthy";
        public float score;
        public string reason = "";

        public ColonyChainSnapshot()
        {
        }

        public ColonyChainSnapshot(string state, float score, string reason)
        {
            this.state = state ?? "healthy";
            this.score = Mathf.Clamp01(score);
            this.reason = reason ?? "";
        }
    }

    public sealed class ColonyDeepSnapshot
    {
        public int ticksGame;
        public int colonists;
        public int downedColonists;
        public int mentalStateColonists;
        public int injuredColonists;
        public int lifeThreatenedColonists;
        public int tendableColonists;
        public int deadColonists;
        public float averageMood;
        public float playerWealth;
        public float threatPoints;
        public float humanEdibleNutrition;
        public int medicineCount;
        public int silverCount;
        public float combatCapacity;
        public float hostileSampleCapacity;
        public float superPawnBonus;
        public bool hasSuperPawn;
        public ColonyChainSnapshot medicalChain = new ColonyChainSnapshot();
        public ColonyChainSnapshot combatChain = new ColonyChainSnapshot();
        public ColonyChainSnapshot foodChain = new ColonyChainSnapshot();
        public ColonyChainSnapshot moraleChain = new ColonyChainSnapshot();
        public ColonyChainSnapshot constructionChain = new ColonyChainSnapshot();
        public ColonyChainSnapshot plantsChain = new ColonyChainSnapshot();

        public static ColonyDeepSnapshot Capture(Map map)
        {
            ColonyDeepSnapshot snapshot = new ColonyDeepSnapshot();
            snapshot.ticksGame = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            if (map == null)
            {
                snapshot.averageMood = 0.5f;
                snapshot.humanEdibleNutrition = -1f;
                return snapshot;
            }

            snapshot.playerWealth = map.PlayerWealthForStoryteller;
            snapshot.threatPoints = StorytellerUtility.DefaultThreatPointsNow(map);
            snapshot.humanEdibleNutrition = map.resourceCounter == null ? -1f : map.resourceCounter.TotalHumanEdibleNutrition;
            snapshot.medicineCount = CountThing(map, "MedicineHerbal") + CountThing(map, "MedicineIndustrial") + CountThing(map, "MedicineUltratech");
            snapshot.silverCount = CountThing(map, "Silver");

            List<Pawn> colonists = map.PlayerPawnsForStoryteller
                .Where(pawn => pawn != null && !pawn.Dead)
                .ToList();
            snapshot.colonists = colonists.Count;
            snapshot.downedColonists = colonists.Count(pawn => pawn.Downed);
            snapshot.mentalStateColonists = colonists.Count(pawn => pawn.InMentalState);
            snapshot.injuredColonists = colonists.Count(HasMeaningfulInjury);
            snapshot.lifeThreatenedColonists = colonists.Count(HasLifeThreateningCondition);
            snapshot.tendableColonists = colonists.Count(NeedsTending);

            List<float> moods = colonists
                .Where(pawn => pawn.needs != null && pawn.needs.mood != null)
                .Select(pawn => pawn.needs.mood.CurLevel)
                .ToList();
            snapshot.averageMood = moods.Count == 0 ? 0.5f : moods.Average();

            List<Pawn> availableColonists = colonists.Where(IsAvailableForWork).ToList();
            snapshot.combatCapacity = availableColonists.Sum(CombatScore);
            float bestCombat = availableColonists.Count == 0 ? 0f : availableColonists.Max(CombatScore);
            float averageCombat = availableColonists.Count == 0 ? 0f : snapshot.combatCapacity / availableColonists.Count;
            snapshot.hasSuperPawn = bestCombat >= 45f && bestCombat >= averageCombat * 1.8f;
            snapshot.superPawnBonus = snapshot.hasSuperPawn ? Mathf.Min(20f, bestCombat * 0.25f) : 0f;

            List<Pawn> hostiles = map.mapPawns == null
                ? new List<Pawn>()
                : map.mapPawns.AllPawnsSpawned
                    .Where(pawn => pawn != null && !pawn.Dead && pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer))
                    .Take(5)
                    .ToList();
            snapshot.hostileSampleCapacity = hostiles.Sum(CombatScore);

            snapshot.medicalChain = BuildSkillChain(availableColonists, SkillDefOf.Medicine, snapshot.medicineCount > 0, snapshot.tendableColonists, "medical");
            snapshot.combatChain = BuildCombatChain(colonists, snapshot.combatCapacity, snapshot.hasSuperPawn);
            snapshot.foodChain = BuildSkillChain(availableColonists, SkillDefOf.Cooking, snapshot.humanEdibleNutrition > Mathf.Max(1f, snapshot.colonists * 0.4f), 0, "food");
            snapshot.moraleChain = BuildMoraleChain(snapshot.averageMood, snapshot.mentalStateColonists, snapshot.colonists);
            snapshot.constructionChain = BuildSkillChain(availableColonists, SkillDefOf.Construction, true, 0, "construction");
            snapshot.plantsChain = BuildSkillChain(availableColonists, SkillDefOf.Plants, true, 0, "plants");
            return snapshot;
        }

        private static int CountThing(Map map, string defName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null || map.resourceCounter == null)
            {
                return 0;
            }

            return map.resourceCounter.GetCount(def);
        }

        private static bool IsAvailableForWork(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Downed && !pawn.InMentalState;
        }

        private static bool HasMeaningfulInjury(Pawn pawn)
        {
            return pawn != null
                && pawn.health != null
                && pawn.health.hediffSet != null
                && (pawn.health.hediffSet.PainTotal > 0.08f || pawn.health.hediffSet.BleedRateTotal > 0.01f || NeedsTending(pawn));
        }

        private static bool HasLifeThreateningCondition(Pawn pawn)
        {
            return pawn != null
                && pawn.health != null
                && pawn.health.hediffSet != null
                && pawn.health.hediffSet.hediffs.Any(hediff => hediff != null && hediff.IsCurrentlyLifeThreatening);
        }

        private static bool NeedsTending(Pawn pawn)
        {
            return pawn != null
                && pawn.health != null
                && pawn.health.hediffSet != null
                && pawn.health.hediffSet.hediffs.Any(hediff => hediff != null && hediff.TendableNow());
        }

        private static float CombatScore(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed)
            {
                return 0f;
            }

            float shooting = SkillLevel(pawn, SkillDefOf.Shooting);
            float melee = SkillLevel(pawn, SkillDefOf.Melee);
            float skill = Mathf.Max(shooting, melee) * 2.2f + Mathf.Min(shooting, melee) * 0.7f;
            float capacity = CapacityLevel(pawn, PawnCapacityDefOf.Consciousness)
                * CapacityLevel(pawn, PawnCapacityDefOf.Moving)
                * CapacityLevel(pawn, PawnCapacityDefOf.Manipulation)
                * CapacityLevel(pawn, PawnCapacityDefOf.Sight);
            float equipment = pawn.equipment != null && pawn.equipment.Primary != null ? 8f : 0f;
            float painPenalty = pawn.health != null && pawn.health.hediffSet != null ? pawn.health.hediffSet.PainTotal * 18f : 0f;
            float recordBonus = ConservativeCombatRecordBonus(pawn);
            return Mathf.Max(0f, skill * Mathf.Clamp(capacity, 0.15f, 1.25f) + equipment + recordBonus - painPenalty);
        }

        private static float ConservativeCombatRecordBonus(Pawn pawn)
        {
            if (pawn == null || pawn.records == null)
            {
                return 0f;
            }

            float kills = SafeRecordValue(pawn, "Kills");
            float damage = SafeRecordValue(pawn, "DamageDealt");
            return Mathf.Min(8f, kills * 0.6f + damage / 800f);
        }

        private static float SafeRecordValue(Pawn pawn, string defName)
        {
            RecordDef def = DefDatabase<RecordDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return 0f;
            }

            return pawn.records.GetValue(def);
        }

        private static float SkillLevel(Pawn pawn, SkillDef skillDef)
        {
            if (pawn == null || pawn.skills == null || skillDef == null)
            {
                return 0f;
            }

            SkillRecord skill = pawn.skills.GetSkill(skillDef);
            if (skill == null || skill.TotallyDisabled)
            {
                return 0f;
            }

            return skill.Level;
        }

        private static float CapacityLevel(Pawn pawn, PawnCapacityDef capacity)
        {
            if (pawn == null || pawn.health == null || pawn.health.capacities == null || capacity == null)
            {
                return 1f;
            }

            if (!pawn.health.capacities.CapableOf(capacity))
            {
                return 0f;
            }

            return pawn.health.capacities.GetLevel(capacity);
        }

        private static ColonyChainSnapshot BuildSkillChain(List<Pawn> availableColonists, SkillDef skillDef, bool hasCriticalResource, int activeDemand, string label)
        {
            float best = availableColonists.Count == 0 ? 0f : availableColonists.Max(pawn => SkillLevel(pawn, skillDef));
            float score = Mathf.Clamp01(best / 10f);
            if (!hasCriticalResource)
            {
                score *= 0.55f;
            }
            if (activeDemand > 0 && best < 6f)
            {
                score *= 0.5f;
            }

            string state = score < 0.25f ? "broken" : score < 0.55f ? "impaired" : "healthy";
            string reason = label + " bestSkill=" + best.ToString("F0") + ", resource=" + hasCriticalResource + ", demand=" + activeDemand;
            return new ColonyChainSnapshot(state, score, reason);
        }

        private static ColonyChainSnapshot BuildCombatChain(List<Pawn> colonists, float combatCapacity, bool hasSuperPawn)
        {
            int available = colonists.Count(IsAvailableForWork);
            float perCapita = colonists.Count == 0 ? 0f : combatCapacity / colonists.Count;
            float score = Mathf.Clamp01(perCapita / 18f + (hasSuperPawn ? 0.2f : 0f));
            if (available <= 0)
            {
                score = 0f;
            }
            string state = score < 0.25f ? "broken" : score < 0.55f ? "impaired" : "healthy";
            return new ColonyChainSnapshot(state, score, "availableFighters=" + available + ", combatCapacity=" + combatCapacity.ToString("F0") + ", superPawn=" + hasSuperPawn);
        }

        private static ColonyChainSnapshot BuildMoraleChain(float averageMood, int mentalStateColonists, int colonists)
        {
            float score = Mathf.Clamp01(averageMood);
            if (mentalStateColonists > 0)
            {
                score -= Mathf.Min(0.5f, mentalStateColonists * 0.18f);
            }
            score = Mathf.Clamp01(score);
            string state = score < 0.25f ? "broken" : score < 0.45f ? "impaired" : "healthy";
            return new ColonyChainSnapshot(state, score, "averageMood=" + averageMood.ToStringPercent() + ", mentalBreaks=" + mentalStateColonists + "/" + colonists);
        }
    }
}
