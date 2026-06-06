using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaNarrativeObservationCandidate
    {
        public string kind = "";
        public string source = "colony_observation";
        public string title = "";
        public string body = "";
        public string defName = "";
        public string cooldownKey = "";
        public int baseImportance;

        public OrcaNarrativeBeat ToBeat(OrcaNarrativeEvaluation evaluation)
        {
            string evaluationDetails = "Narrative evaluation:\n"
                + "Classification: " + evaluation.classification + "\n"
                + "Score: " + evaluation.score.ToString("F0") + "\n"
                + "SpeakChance: " + evaluation.speakChance.ToStringPercent() + "\n"
                + "DominantTheme: " + evaluation.dominantTheme + "\n"
                + "Reasons: " + string.Join("; ", evaluation.reasons.ToArray()) + "\n"
                + "ChainScores: " + evaluation.chainScores + "\n";

            return new OrcaNarrativeBeat(
                source,
                title.NullOrEmpty() ? evaluation.dominantTheme : title,
                body + "\n" + evaluationDetails,
                Mathf.RoundToInt(evaluation.score),
                cooldownKey.NullOrEmpty() ? source + ":" + kind + ":" + title : cooldownKey);
        }
    }

    public sealed class OrcaNarrativeEvaluation
    {
        public string classification = "quiet_growth";
        public float score;
        public float speakChance;
        public string dominantTheme = "";
        public List<string> reasons = new List<string>();
        public string chainScores = "";
        public OrcaNarrativeObservationCandidate candidate;
        public bool worsening;

        public bool ShouldAlwaysSpeak
        {
            get { return classification == "negative_danger" || speakChance >= 1f; }
        }
    }

    public sealed class OrcaNarrativeEvaluationState
    {
        public string lastWorseningKey = "";
        public int consecutiveWorseningMisses;

        public void RecordResult(OrcaNarrativeEvaluation evaluation, bool spoke)
        {
            if (evaluation == null || !evaluation.worsening)
            {
                if (spoke)
                {
                    lastWorseningKey = "";
                    consecutiveWorseningMisses = 0;
                }
                return;
            }

            string key = evaluation.classification + ":" + evaluation.dominantTheme;
            if (spoke)
            {
                lastWorseningKey = "";
                consecutiveWorseningMisses = 0;
            }
            else if (key == lastWorseningKey)
            {
                consecutiveWorseningMisses++;
            }
            else
            {
                lastWorseningKey = key;
                consecutiveWorseningMisses = 1;
            }
        }
    }

    public static class OrcaNarrativeEvaluator
    {
        public static OrcaNarrativeEvaluation EvaluateCurrentStateForTool(ColonyDeepSnapshot current)
        {
            if (current == null)
            {
                return Quiet();
            }

            OrcaNarrativeObservationCandidate candidate = new OrcaNarrativeObservationCandidate
            {
                kind = "state_negative",
                source = "tool_colony_summary",
                title = "Current colony assessment",
                body = "Current colony state assessment requested by a tool call.",
                defName = "current_colony_state",
                baseImportance = 55,
                cooldownKey = "tool:current_colony_state"
            };

            OrcaNarrativeEvaluation evaluation;
            float vulnerability = VulnerabilityScore(current);
            if (IsQuietGrowth(current))
            {
                evaluation = Base(candidate, "Ordinary day");
                evaluation.classification = "quiet_growth";
                evaluation.score = 0f;
                evaluation.speakChance = 0f;
                evaluation.reasons.Add("stable resources and no immediate pressure");
            }
            else
            {
                evaluation = EvaluateStateNegative(candidate, null, current);
                if (vulnerability < 30f && current.combatChain != null && current.combatChain.state == "healthy")
                {
                    evaluation.classification = "contained";
                    evaluation.score = Mathf.Min(evaluation.score, 25f);
                    evaluation.speakChance = 0f;
                    evaluation.dominantTheme = "Pressure contained";
                    evaluation.reasons.Add("pressure exists but key chains are available");
                }
            }

            evaluation.chainScores = ChainScores(current);
            return evaluation;
        }

        public static OrcaNarrativeEvaluation Evaluate(
            List<OrcaNarrativeObservationCandidate> candidates,
            ColonyDeepSnapshot previous,
            ColonyDeepSnapshot current,
            OrcaNarrativeEvaluationState state)
        {
            if (current == null || candidates == null || candidates.Count == 0)
            {
                return Quiet();
            }

            OrcaNarrativeEvaluation best = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                OrcaNarrativeEvaluation evaluation = EvaluateCandidate(candidates[i], previous, current);
                if (best == null || evaluation.score > best.score)
                {
                    best = evaluation;
                }
            }

            if (best == null)
            {
                return Quiet();
            }

            if (best.worsening && state != null && state.consecutiveWorseningMisses >= 2 && best.score >= 35f)
            {
                best.score = Mathf.Max(best.score, 70f);
                best.speakChance = Mathf.Max(best.speakChance, 0.85f);
                best.reasons.Add("continuous worsening missed " + state.consecutiveWorseningMisses + " time(s)");
            }

            if (best.worsening && state != null && state.consecutiveWorseningMisses >= 4)
            {
                best.score = Mathf.Max(best.score, 85f);
                best.speakChance = 1f;
                best.classification = best.classification == "negative_medium" ? "negative_big" : best.classification;
                best.reasons.Add("continuous worsening guarantee");
            }

            return best;
        }

        private static OrcaNarrativeEvaluation EvaluateCandidate(OrcaNarrativeObservationCandidate candidate, ColonyDeepSnapshot previous, ColonyDeepSnapshot current)
        {
            string kind = candidate.kind ?? "";
            if (kind == "threat")
            {
                return EvaluateThreat(candidate, previous, current);
            }
            if (kind == "opportunity")
            {
                return EvaluateOpportunity(candidate, previous, current);
            }
            if (kind == "positive")
            {
                return EvaluatePositive(candidate, previous, current);
            }
            if (kind == "death")
            {
                return EvaluateDeath(candidate, previous, current);
            }
            if (kind == "medical")
            {
                return EvaluateMedical(candidate, previous, current);
            }
            if (kind == "state_negative")
            {
                return EvaluateStateNegative(candidate, previous, current);
            }
            if (kind == "recovery")
            {
                return EvaluateRecovery(candidate, previous, current);
            }

            return EvaluateGeneric(candidate, previous, current);
        }

        private static OrcaNarrativeEvaluation EvaluateThreat(OrcaNarrativeObservationCandidate candidate, ColonyDeepSnapshot previous, ColonyDeepSnapshot current)
        {
            OrcaNarrativeEvaluation result = Base(candidate, "Threat pressure");
            float vulnerability = VulnerabilityScore(current);
            float combatReadiness = Mathf.Clamp(current.combatCapacity + current.superPawnBonus, 0f, 160f);
            float hostilePressure = current.hostileSampleCapacity > 0f ? current.hostileSampleCapacity * 1.15f : current.threatPoints / 18f + candidate.baseImportance * 0.35f;
            float history = OrcaNarrativeHistoryMemory.SimilarThreatModifier("incident", candidate.defName, current.threatPoints);
            float score = 45f + hostilePressure + vulnerability * 0.6f + history - combatReadiness * 0.45f;
            if (current.hasSuperPawn)
            {
                score -= 10f;
                result.reasons.Add("super pawn detected");
            }
            if (current.combatChain != null && current.combatChain.state == "broken")
            {
                score += 18f;
            }
            if (current.medicalChain != null && current.medicalChain.state == "broken")
            {
                score += 10f;
            }

            result.score = Mathf.Clamp(score, 0f, 100f);
            if (result.score < 30f)
            {
                result.classification = "easy_fight";
                result.speakChance = 0f;
                result.dominantTheme = "Another manageable fight";
            }
            else if (result.score < 55f)
            {
                result.classification = "fair_fight";
                result.speakChance = ChanceForScore(result.score);
                result.dominantTheme = "A fair test";
            }
            else if (result.score < 85f)
            {
                result.classification = "difficult_fight";
                result.speakChance = ChanceForScore(result.score);
                result.dominantTheme = "No time to recover";
            }
            else
            {
                result.classification = "negative_danger";
                result.speakChance = 1f;
                result.dominantTheme = "Desperate situation";
            }

            result.reasons.Add("hostilePressure=" + hostilePressure.ToString("F0"));
            result.reasons.Add("combatCapacity=" + current.combatCapacity.ToString("F0"));
            result.reasons.Add("vulnerability=" + vulnerability.ToString("F0"));
            if (history != 0f)
            {
                result.reasons.Add("historyModifier=" + history.ToString("F0"));
            }
            result.chainScores = ChainScores(current);
            result.worsening = result.score >= 55f || Worsened(previous, current);
            return result;
        }

        private static OrcaNarrativeEvaluation EvaluateOpportunity(OrcaNarrativeObservationCandidate candidate, ColonyDeepSnapshot previous, ColonyDeepSnapshot current)
        {
            OrcaNarrativeEvaluation result = Base(candidate, "Opportunity under pressure");
            float shortage = ShortageScore(current);
            bool trader = TextContains(candidate, "trader") || TextContains(candidate, "caravan");
            bool cargo = TextContains(candidate, "cargo") || TextContains(candidate, "pod");
            float actionability = trader ? 28f : cargo ? 14f : 10f;
            float score = candidate.baseImportance * 0.35f + shortage * 0.85f + actionability;
            bool lifesaving = shortage >= 45f && MentionsRescueResource(candidate);
            if (lifesaving)
            {
                score += 18f;
            }

            result.score = Mathf.Clamp(score, 0f, 100f);
            if (trader && shortage >= 25f)
            {
                result.classification = "dilemma_medium";
                result.dominantTheme = "Possible turning point and moral dilemma";
            }
            else if (lifesaving)
            {
                result.classification = "positive_big";
                result.dominantTheme = "Timely relief";
            }
            else if (result.score < 30f)
            {
                result.classification = "positive_small";
                result.dominantTheme = "Minor windfall";
                result.speakChance = 0f;
                result.reasons.Add("low pressure opportunity");
                result.chainScores = ChainScores(current);
                return result;
            }
            else
            {
                result.classification = result.score >= 55f ? "positive_big" : "positive_medium";
                result.dominantTheme = result.score >= 55f ? "Timely relief" : "Recovery period";
            }

            result.speakChance = ChanceForScore(result.score);
            result.reasons.Add("shortage=" + shortage.ToString("F0"));
            result.reasons.Add("actionability=" + actionability.ToString("F0"));
            result.chainScores = ChainScores(current);
            result.worsening = shortage >= 35f && Worsened(previous, current);
            return result;
        }

        private static OrcaNarrativeEvaluation EvaluatePositive(OrcaNarrativeObservationCandidate candidate, ColonyDeepSnapshot previous, ColonyDeepSnapshot current)
        {
            OrcaNarrativeEvaluation result = Base(candidate, "Positive event");
            float shortage = ShortageScore(current);
            bool lifesaving = shortage >= 45f && MentionsRescueResource(candidate);
            result.score = lifesaving ? 70f : Mathf.Clamp(candidate.baseImportance * 0.45f + shortage * 0.4f, 0f, 65f);
            if (lifesaving)
            {
                result.classification = "positive_big";
                result.speakChance = ChanceForScore(result.score);
                result.dominantTheme = "Timely relief";
            }
            else if (result.score < 30f)
            {
                result.classification = "positive_small";
                result.speakChance = 0f;
                result.dominantTheme = "Minor windfall";
            }
            else
            {
                result.classification = "positive_medium";
                result.speakChance = ChanceForScore(result.score) * 0.75f;
                result.dominantTheme = "Recovery period";
            }
            result.reasons.Add("shortage=" + shortage.ToString("F0"));
            result.chainScores = ChainScores(current);
            return result;
        }

        private static OrcaNarrativeEvaluation EvaluateDeath(OrcaNarrativeObservationCandidate candidate, ColonyDeepSnapshot previous, ColonyDeepSnapshot current)
        {
            OrcaNarrativeEvaluation result = Base(candidate, "Death observed");
            float vulnerability = VulnerabilityScore(current);
            bool chainsBroken = ChainBroken(current.medicalChain) || ChainBroken(current.foodChain) || ChainBroken(current.moraleChain) || ChainBroken(current.combatChain);
            result.score = chainsBroken ? Mathf.Clamp(82f + vulnerability * 0.25f, 0f, 100f) : Mathf.Clamp(52f + vulnerability * 0.15f, 0f, 80f);
            if (result.score >= 85f)
            {
                result.classification = "negative_danger";
                result.speakChance = 1f;
                result.dominantTheme = "Desperate situation";
                result.worsening = true;
            }
            else
            {
                result.classification = "positive_medium";
                result.speakChance = ChanceForScore(result.score) * 0.9f;
                result.dominantTheme = "A colonist has fallen";
            }
            result.reasons.Add("irreversible loss");
            result.reasons.Add("vulnerability=" + vulnerability.ToString("F0"));
            result.chainScores = ChainScores(current);
            return result;
        }

        private static OrcaNarrativeEvaluation EvaluateMedical(OrcaNarrativeObservationCandidate candidate, ColonyDeepSnapshot previous, ColonyDeepSnapshot current)
        {
            OrcaNarrativeEvaluation result = Base(candidate, "Medical pressure");
            bool contained = current.medicalChain != null && current.medicalChain.state == "healthy" && current.medicineCount > 0 && current.lifeThreatenedColonists == 0 && current.averageMood > 0.35f;
            if (contained)
            {
                result.classification = "contained";
                result.score = 0f;
                result.speakChance = 0f;
                result.dominantTheme = "Pressure contained";
                result.reasons.Add("medical chain available");
                result.chainScores = ChainScores(current);
                return result;
            }

            float vulnerability = VulnerabilityScore(current);
            result.score = Mathf.Clamp(48f + vulnerability * 0.55f + current.tendableColonists * 5f + current.lifeThreatenedColonists * 12f, 0f, 100f);
            if (ChainBroken(current.medicalChain) || current.lifeThreatenedColonists > 0 && current.medicineCount == 0)
            {
                result.classification = "negative_danger";
                result.speakChance = 1f;
                result.dominantTheme = "The colony needs time to recover";
            }
            else
            {
                result.classification = result.score >= 70f ? "negative_big" : "negative_medium";
                result.speakChance = ChanceForScore(result.score);
                result.dominantTheme = "The colony needs time to recover";
            }
            result.reasons.Add("medicalChain=" + (current.medicalChain == null ? "" : current.medicalChain.state));
            result.reasons.Add("medicine=" + current.medicineCount);
            result.reasons.Add("tendable=" + current.tendableColonists);
            result.chainScores = ChainScores(current);
            result.worsening = true;
            return result;
        }

        private static OrcaNarrativeEvaluation EvaluateStateNegative(OrcaNarrativeObservationCandidate candidate, ColonyDeepSnapshot previous, ColonyDeepSnapshot current)
        {
            OrcaNarrativeEvaluation result = Base(candidate, "Colony pressure");
            float vulnerability = VulnerabilityScore(current);
            float trend = TrendPressure(previous, current);
            result.score = Mathf.Clamp(candidate.baseImportance * 0.35f + vulnerability * 0.7f + trend, 0f, 100f);
            result.classification = result.score >= 85f ? "negative_danger" : result.score >= 65f ? "negative_big" : "negative_medium";
            result.speakChance = result.classification == "negative_danger" ? 1f : ChanceForScore(result.score);
            result.dominantTheme = result.classification == "negative_danger" ? "Desperate situation" : "Things are starting to go wrong";
            result.reasons.Add("vulnerability=" + vulnerability.ToString("F0"));
            result.reasons.Add("trendPressure=" + trend.ToString("F0"));
            result.chainScores = ChainScores(current);
            result.worsening = Worsened(previous, current) || result.score >= 65f;
            return result;
        }

        private static OrcaNarrativeEvaluation EvaluateRecovery(OrcaNarrativeObservationCandidate candidate, ColonyDeepSnapshot previous, ColonyDeepSnapshot current)
        {
            OrcaNarrativeEvaluation result = Base(candidate, "Recovery");
            float improvement = RecoveryScore(previous, current);
            float stillUnsafe = VulnerabilityScore(current);
            result.score = Mathf.Clamp(35f + improvement * 0.45f - stillUnsafe * 0.2f, 0f, 70f);
            result.classification = result.score < 30f ? "quiet_growth" : "positive_medium";
            result.speakChance = result.classification == "quiet_growth" ? 0f : ChanceForScore(result.score) * 0.6f;
            result.dominantTheme = "Recovery period";
            result.reasons.Add("improvement=" + improvement.ToString("F0"));
            result.reasons.Add("stillUnsafe=" + stillUnsafe.ToString("F0"));
            result.chainScores = ChainScores(current);
            return result;
        }

        private static OrcaNarrativeEvaluation EvaluateGeneric(OrcaNarrativeObservationCandidate candidate, ColonyDeepSnapshot previous, ColonyDeepSnapshot current)
        {
            OrcaNarrativeEvaluation result = Base(candidate, "Generic observation");
            float vulnerability = VulnerabilityScore(current);
            result.score = Mathf.Clamp(candidate.baseImportance * 0.4f + vulnerability * 0.4f, 0f, 75f);
            result.classification = result.score < 30f ? "quiet_growth" : "negative_medium";
            result.speakChance = result.classification == "quiet_growth" ? 0f : ChanceForScore(result.score);
            result.dominantTheme = result.classification == "quiet_growth" ? "Ordinary day" : "Things are starting to go wrong";
            result.reasons.Add("generic observation");
            result.chainScores = ChainScores(current);
            return result;
        }

        private static OrcaNarrativeEvaluation Base(OrcaNarrativeObservationCandidate candidate, string theme)
        {
            OrcaNarrativeEvaluation result = new OrcaNarrativeEvaluation();
            result.candidate = candidate;
            result.dominantTheme = theme;
            result.reasons.Add("candidate=" + (candidate == null ? "" : candidate.kind + "/" + candidate.defName));
            return result;
        }

        private static OrcaNarrativeEvaluation Quiet()
        {
            OrcaNarrativeEvaluation result = new OrcaNarrativeEvaluation();
            result.classification = "quiet_growth";
            result.score = 0f;
            result.speakChance = 0f;
            result.dominantTheme = "Ordinary day";
            return result;
        }

        private static float ChanceForScore(float score)
        {
            if (score < 30f)
            {
                return 0f;
            }
            if (score >= 85f)
            {
                return 1f;
            }
            if (score < 55f)
            {
                return Mathf.Lerp(0.2f, 0.45f, (score - 30f) / 25f);
            }
            return Mathf.Lerp(0.55f, 0.85f, (score - 55f) / 30f);
        }

        private static float VulnerabilityScore(ColonyDeepSnapshot current)
        {
            if (current == null)
            {
                return 0f;
            }

            float score = 0f;
            if (current.humanEdibleNutrition >= 0f)
            {
                if (current.humanEdibleNutrition <= 0.5f)
                {
                    score += 25f;
                }
                else if (current.humanEdibleNutrition <= Mathf.Max(2f, current.colonists * 0.5f))
                {
                    score += 14f;
                }
            }
            if (current.medicineCount <= 0 && current.tendableColonists > 0)
            {
                score += 18f;
            }
            score += current.downedColonists * 12f;
            score += current.mentalStateColonists * 10f;
            score += current.injuredColonists * 4f;
            score += current.lifeThreatenedColonists * 15f;
            if (current.averageMood < 0.25f)
            {
                score += 18f;
            }
            else if (current.averageMood < 0.35f)
            {
                score += 10f;
            }
            if (ChainBroken(current.medicalChain))
            {
                score += 14f;
            }
            if (ChainBroken(current.moraleChain))
            {
                score += 10f;
            }
            if (ChainBroken(current.foodChain))
            {
                score += 10f;
            }
            return Mathf.Clamp(score, 0f, 100f);
        }

        private static float ShortageScore(ColonyDeepSnapshot current)
        {
            if (current == null)
            {
                return 0f;
            }

            float score = 0f;
            if (current.humanEdibleNutrition <= 0.5f)
            {
                score += 35f;
            }
            else if (current.humanEdibleNutrition <= Mathf.Max(2f, current.colonists * 0.5f))
            {
                score += 20f;
            }
            if (current.medicineCount <= 0 && (current.tendableColonists > 0 || current.lifeThreatenedColonists > 0))
            {
                score += 28f;
            }
            if (current.averageMood < 0.3f)
            {
                score += 12f;
            }
            if (current.mentalStateColonists > 0)
            {
                score += 8f;
            }
            return Mathf.Clamp(score, 0f, 100f);
        }

        private static float TrendPressure(ColonyDeepSnapshot previous, ColonyDeepSnapshot current)
        {
            if (previous == null || current == null)
            {
                return 0f;
            }

            float score = 0f;
            if (current.humanEdibleNutrition < previous.humanEdibleNutrition)
            {
                score += Mathf.Min(18f, (previous.humanEdibleNutrition - current.humanEdibleNutrition) * 3f);
            }
            if (current.averageMood < previous.averageMood)
            {
                score += Mathf.Min(20f, (previous.averageMood - current.averageMood) * 80f);
            }
            if (current.mentalStateColonists > previous.mentalStateColonists)
            {
                score += (current.mentalStateColonists - previous.mentalStateColonists) * 12f;
            }
            if (current.downedColonists > previous.downedColonists)
            {
                score += (current.downedColonists - previous.downedColonists) * 14f;
            }
            return Mathf.Clamp(score, 0f, 60f);
        }

        private static float RecoveryScore(ColonyDeepSnapshot previous, ColonyDeepSnapshot current)
        {
            if (previous == null || current == null)
            {
                return 0f;
            }

            float score = 0f;
            if (current.humanEdibleNutrition > previous.humanEdibleNutrition)
            {
                score += Mathf.Min(25f, (current.humanEdibleNutrition - previous.humanEdibleNutrition) * 3f);
            }
            if (current.medicineCount > previous.medicineCount)
            {
                score += Mathf.Min(15f, (current.medicineCount - previous.medicineCount) * 5f);
            }
            if (current.averageMood > previous.averageMood)
            {
                score += Mathf.Min(20f, (current.averageMood - previous.averageMood) * 80f);
            }
            if (current.mentalStateColonists < previous.mentalStateColonists)
            {
                score += (previous.mentalStateColonists - current.mentalStateColonists) * 10f;
            }
            if (current.downedColonists < previous.downedColonists)
            {
                score += (previous.downedColonists - current.downedColonists) * 10f;
            }
            return Mathf.Clamp(score, 0f, 80f);
        }

        private static bool Worsened(ColonyDeepSnapshot previous, ColonyDeepSnapshot current)
        {
            return TrendPressure(previous, current) >= 18f;
        }

        private static bool ChainBroken(ColonyChainSnapshot chain)
        {
            return chain != null && chain.state == "broken";
        }

        private static bool IsQuietGrowth(ColonyDeepSnapshot current)
        {
            if (current == null)
            {
                return true;
            }

            return current.downedColonists == 0
                && current.mentalStateColonists == 0
                && current.lifeThreatenedColonists == 0
                && current.tendableColonists == 0
                && current.averageMood >= 0.45f
                && current.humanEdibleNutrition > Mathf.Max(3f, current.colonists * 0.8f)
                && current.medicalChain != null && current.medicalChain.state == "healthy"
                && current.combatChain != null && current.combatChain.state == "healthy"
                && current.foodChain != null && current.foodChain.state == "healthy"
                && current.moraleChain != null && current.moraleChain.state == "healthy";
        }

        private static bool TextContains(OrcaNarrativeObservationCandidate candidate, string value)
        {
            if (candidate == null || value.NullOrEmpty())
            {
                return false;
            }

            string haystack = ((candidate.defName ?? "") + " " + (candidate.title ?? "") + " " + (candidate.body ?? "")).ToLowerInvariant();
            return haystack.Contains(value.ToLowerInvariant());
        }

        private static bool MentionsRescueResource(OrcaNarrativeObservationCandidate candidate)
        {
            return TextContains(candidate, "food")
                || TextContains(candidate, "meal")
                || TextContains(candidate, "medicine");
        }

        private static string ChainScores(ColonyDeepSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "";
            }

            List<string> parts = new List<string>();
            AddChain(parts, "medical", snapshot.medicalChain);
            AddChain(parts, "combat", snapshot.combatChain);
            AddChain(parts, "food", snapshot.foodChain);
            AddChain(parts, "morale", snapshot.moraleChain);
            AddChain(parts, "construction", snapshot.constructionChain);
            AddChain(parts, "plants", snapshot.plantsChain);
            return string.Join(", ", parts.ToArray());
        }

        private static void AddChain(List<string> parts, string label, ColonyChainSnapshot chain)
        {
            if (chain == null)
            {
                parts.Add(label + "=unknown");
                return;
            }

            parts.Add(label + "=" + chain.state + "(" + chain.score.ToStringPercent() + ")");
        }
    }
}
