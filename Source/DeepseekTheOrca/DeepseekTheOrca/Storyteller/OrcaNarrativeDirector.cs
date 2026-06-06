using System.Collections.Generic;
using System.Linq;
using DeepseekTheOrca.Rimtalk;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaNarrativeBeat
    {
        public string source;
        public string title;
        public string body;
        public string cooldownKey;
        public int importance;
        public bool openChatWindow = true;

        public OrcaNarrativeBeat(string source, string title, string body, int importance, string cooldownKey)
        {
            this.source = source ?? "";
            this.title = title ?? "";
            this.body = body ?? "";
            this.importance = importance;
            this.cooldownKey = cooldownKey ?? this.source + ":" + this.title;
        }
    }

    public interface IOrcaNarrativeBeatSource
    {
        void Tick();
    }

    public sealed class OrcaNarrativeDirectorSource : IOrcaProactiveConversationSource
    {
        public void Tick()
        {
            OrcaNarrativeDirector.Tick();
        }
    }

    public static class OrcaNarrativeDirector
    {
        private const int MaxQueuedBeats = 20;
        private const int MinImportance = 40;
        private const int GlobalCooldownTicks = 4500;
        private const int BeatCooldownTicks = 18000;
        private static readonly Queue<OrcaNarrativeBeat> pendingBeats = new Queue<OrcaNarrativeBeat>();
        private static readonly Dictionary<string, int> lastBeatTicksByKey = new Dictionary<string, int>();
        private static readonly List<IOrcaNarrativeBeatSource> sources = new List<IOrcaNarrativeBeatSource>();
        private static bool defaultsRegistered;
        private static int lastDispatchTick = -999999;

        public static void RegisterSource(IOrcaNarrativeBeatSource source)
        {
            EnsureDefaultsRegistered();
            if (source != null && !sources.Contains(source))
            {
                sources.Add(source);
            }
        }

        public static bool EnqueueBeat(OrcaNarrativeBeat beat)
        {
            if (beat == null || beat.importance < MinImportance)
            {
                return false;
            }

            int ticksGame = CurrentTick();
            int lastTick;
            if (!beat.cooldownKey.NullOrEmpty()
                && lastBeatTicksByKey.TryGetValue(beat.cooldownKey, out lastTick)
                && ticksGame - lastTick < BeatCooldownTicks)
            {
                Debug("Narrative beat skipped by cooldown: " + beat.cooldownKey);
                return false;
            }

            if (!beat.cooldownKey.NullOrEmpty())
            {
                lastBeatTicksByKey[beat.cooldownKey] = ticksGame;
            }

            while (pendingBeats.Count >= MaxQueuedBeats)
            {
                pendingBeats.Dequeue();
            }

            pendingBeats.Enqueue(beat);
            Debug("Narrative beat queued: " + beat.source + " | " + beat.title + " | importance=" + beat.importance);
            return true;
        }

        public static bool EnqueueAmbientBeat(OrcaNarrativeBeat beat, float chance)
        {
            if (!OrcaProactiveConversationManager.AmbientEnabled)
            {
                Debug("Ambient proactive beat skipped because ambient proactive dialogue is disabled: " + (beat == null ? "<null>" : beat.source + " | " + beat.title));
                return false;
            }

            chance = Mathf.Clamp01(chance);
            if (chance < 1f && !Rand.Chance(chance))
            {
                Debug("Ambient proactive beat roll skipped at " + chance.ToStringPercent() + ": " + (beat == null ? "<null>" : beat.source + " | " + beat.title));
                return false;
            }

            return EnqueueBeat(beat);
        }

        public static void NotifyStorytellerIncidentScheduled(AiIncidentPlan plan, FiringIncident firingIncident, IIncidentTarget target)
        {
            if (plan == null || firingIncident == null || firingIncident.def == null)
            {
                return;
            }

            OrcaNarrativeHistoryMemory.BeginIncident(firingIncident.def.defName, firingIncident.parms == null ? 0f : firingIncident.parms.points, target as Map);
            string mapText = target == null ? "unknown target" : target.ToString();
            string body = "A non-player-requested storyteller event has just been scheduled by you.\n"
                + "IncidentDef: " + firingIncident.def.defName + "\n"
                + "Target: " + mapText + "\n"
                + "Reason: " + (plan.reason ?? "") + "\n"
                + "As a Game-Master-like narrator, briefly describe the new pressure, consequence, or gift this adds to the story. "
                + "Mention it naturally as something you decided to send. You may ask the player what they intend to do next if that fits the moment. "
                + "Do not expose internal tool or validation details.";

            EnqueueBeat(new OrcaNarrativeBeat(
                "storyteller_incident",
                "Storyteller event scheduled",
                body,
                85,
                "storyteller_incident:" + firingIncident.def.defName));
        }

        public static void Tick()
        {
            EnsureDefaultsRegistered();

            for (int i = 0; i < sources.Count; i++)
            {
                sources[i].Tick();
            }

            if (pendingBeats.Count == 0)
            {
                return;
            }

            int ticksGame = CurrentTick();
            if (ticksGame - lastDispatchTick < GlobalCooldownTicks)
            {
                return;
            }

            OrcaNarrativeBeat beat = pendingBeats.Dequeue();
            lastDispatchTick = ticksGame;
            OrcaProactiveConversationManager.Enqueue(ToRequest(beat));
        }

        private static OrcaProactiveConversationRequest ToRequest(OrcaNarrativeBeat beat)
        {
            string body = "Narrative beat source: " + beat.source + "\n"
                + "Current game language: " + OrcaLanguageUtility.CurrentGameLanguage() + "\n"
                + "Title: " + beat.title + "\n"
                + "Importance: " + beat.importance + "\n"
                + "Details:\n" + beat.body + "\n"
                + RimtalkIntegration.NarrativeContextInstruction()
                + "Speak proactively as the current persona in the current game language. Act like a concise Game Master: describe the situation, name the tension or consequence, and ask the player what they intend to do only if it is useful. "
                + "Do not turn this into a long scene unless the beat demands it.";

            OrcaProactiveConversationRequest request = new OrcaProactiveConversationRequest(beat.source, beat.title, body);
            request.openChatWindow = beat.openChatWindow;
            return request;
        }

        private static void EnsureDefaultsRegistered()
        {
            if (defaultsRegistered)
            {
                return;
            }

            defaultsRegistered = true;
            sources.Add(new ColonyObservationNarrativeSource());
        }

        private static int CurrentTick()
        {
            return Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
        }

        private static void Debug(string message)
        {
            if (DeepseekTheOrcaMod.Settings != null && DeepseekTheOrcaMod.Settings.debugLogging)
            {
                Log.Message("[RimAgent] " + message);
            }
        }
    }

    public sealed class ColonyObservationNarrativeSource : IOrcaNarrativeBeatSource
    {
        private const int ScanIntervalTicks = 2500;
        private const int MaxSeenKeys = 200;
        private readonly HashSet<string> seenLetterKeys = new HashSet<string>();
        private readonly Queue<string> seenKeyOrder = new Queue<string>();
        private readonly Queue<ColonyDeepSnapshot> recentSnapshots = new Queue<ColonyDeepSnapshot>();
        private readonly OrcaNarrativeEvaluationState evaluationState = new OrcaNarrativeEvaluationState();
        private ColonyDeepSnapshot previous;
        private int lastScanTick = -999999;
        private bool seeded;

        public void Tick()
        {
            if (Find.TickManager == null)
            {
                return;
            }

            int ticksGame = Find.TickManager.TicksGame;
            if (ticksGame - lastScanTick < ScanIntervalTicks)
            {
                return;
            }

            lastScanTick = ticksGame;
            List<Letter> letters = RecentLetters(12);
            ColonyDeepSnapshot current = Find.CurrentMap == null || Find.CurrentMap.StoryState == null ? null : ColonyDeepSnapshot.Capture(Find.CurrentMap);
            if (!seeded)
            {
                MarkSeen(letters);
                previous = current;
                AddRecentSnapshot(current);
                seeded = true;
                return;
            }

            List<OrcaNarrativeObservationCandidate> candidates = new List<OrcaNarrativeObservationCandidate>();
            ColonyDeepSnapshot trendPrevious = recentSnapshots.Count == 0 ? previous : recentSnapshots.Peek();
            CollectDeltas(previous, current, trendPrevious, candidates);

            for (int i = 0; i < letters.Count; i++)
            {
                Letter letter = letters[i];
                string key = LetterKey(letter);
                if (key.NullOrEmpty() || seenLetterKeys.Contains(key))
                {
                    continue;
                }

                AddSeen(key);
                OrcaNarrativeObservationCandidate candidate = BuildCandidate(letter);
                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            }

            EvaluateAndMaybeEnqueue(candidates, trendPrevious, current);
            previous = current;
            AddRecentSnapshot(current);
        }

        private void AddRecentSnapshot(ColonyDeepSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            recentSnapshots.Enqueue(snapshot);
            while (recentSnapshots.Count > 4)
            {
                recentSnapshots.Dequeue();
            }
        }

        private void MarkSeen(List<Letter> letters)
        {
            for (int i = 0; i < letters.Count; i++)
            {
                AddSeen(LetterKey(letters[i]));
            }
        }

        private void AddSeen(string key)
        {
            if (key.NullOrEmpty() || seenLetterKeys.Contains(key))
            {
                return;
            }

            seenLetterKeys.Add(key);
            seenKeyOrder.Enqueue(key);
            while (seenKeyOrder.Count > MaxSeenKeys)
            {
                seenLetterKeys.Remove(seenKeyOrder.Dequeue());
            }
        }

        private static OrcaNarrativeObservationCandidate BuildCandidate(Letter letter)
        {
            if (letter == null)
            {
                return null;
            }

            int importance = LetterImportance(letter);
            if (importance < 40)
            {
                return null;
            }

            string label = SafeResolve(letter.Label);
            string defName = letter.def == null ? "" : letter.def.defName;
            string text = LetterText(letter);
            string faction = letter.relatedFaction == null ? "" : letter.relatedFaction.Name;

            string body = "A new in-game letter appeared.\n"
                + "Label: " + label + "\n"
                + "LetterDef: " + defName + "\n"
                + (faction.NullOrEmpty() ? "" : "Faction: " + faction + "\n")
                + (text.NullOrEmpty() ? "" : "Text: " + text + "\n")
                + "This is a colony observation. Consider it alongside colony state and recent letters. "
                + "As a Game-Master-like narrator, decide whether this is worth commenting on. If it is, describe what changed and what pressure or opportunity it creates.";

            return new OrcaNarrativeObservationCandidate
            {
                kind = LetterKind(letter, label, text),
                source = "colony_observation",
                title = label.NullOrEmpty() ? "New letter" : label,
                body = body,
                defName = defName,
                baseImportance = importance,
                cooldownKey = "letter:" + defName + ":" + label
            };
        }

        private static string LetterKind(Letter letter, string label, string text)
        {
            string defName = letter.def == null ? "" : letter.def.defName;
            string haystack = (defName + " " + label + " " + text).ToLowerInvariant();
            if (haystack.Contains("death") || haystack.Contains("dead") || haystack.Contains("died"))
            {
                return "death";
            }
            if (haystack.Contains("disease") || haystack.Contains("infection") || haystack.Contains("medical") || haystack.Contains("sick"))
            {
                return "medical";
            }
            if (haystack.Contains("raid") || haystack.Contains("threat") || haystack.Contains("manhunter") || haystack.Contains("infestation") || haystack.Contains("mech"))
            {
                return "threat";
            }
            if (haystack.Contains("trader") || haystack.Contains("caravan") || haystack.Contains("visitor") || haystack.Contains("quest"))
            {
                return "opportunity";
            }
            if (haystack.Contains("cargo") || haystack.Contains("pod") || haystack.Contains("positive"))
            {
                return "positive";
            }
            if (haystack.Contains("negative"))
            {
                return "state_negative";
            }
            if (haystack.Contains("neutral"))
            {
                return "opportunity";
            }
            return "generic";
        }

        private static int LetterImportance(Letter letter)
        {
            if (letter.def == null)
            {
                return 45;
            }

            string defName = letter.def.defName.ToLowerInvariant();
            if (defName.Contains("threat") || defName.Contains("negative") || defName.Contains("death") || defName.Contains("quest"))
            {
                return 80;
            }

            if (defName.Contains("neutral") || defName.Contains("positive"))
            {
                return 55;
            }

            return 60;
        }

        private static List<Letter> RecentLetters(int count)
        {
            List<Letter> letters = new List<Letter>();
            if (Find.Archive != null && Find.Archive.ArchivablesListForReading != null)
            {
                letters.AddRange(Find.Archive.ArchivablesListForReading.OfType<Letter>());
            }

            if (Find.LetterStack != null && Find.LetterStack.LettersListForReading != null)
            {
                letters.AddRange(Find.LetterStack.LettersListForReading);
            }

            return letters
                .Where(letter => letter != null)
                .GroupBy(LetterKey)
                .Select(group => group.First())
                .OrderBy(letter => letter.arrivalTick)
                .TakeLastCompat(count)
                .ToList();
        }

        private static string LetterKey(Letter letter)
        {
            if (letter == null)
            {
                return "";
            }

            string label = SafeResolve(letter.Label);
            string defName = letter.def == null ? "" : letter.def.defName;
            return letter.arrivalTick + "|" + defName + "|" + label;
        }

        private static string LetterText(Letter letter)
        {
            ChoiceLetter choiceLetter = letter as ChoiceLetter;
            if (choiceLetter == null)
            {
                return "";
            }

            string text = choiceLetter.Text.Resolve();
            if (!text.NullOrEmpty() && text.Length > 500)
            {
                text = text.Substring(0, 500) + "...";
            }

            return text ?? "";
        }

        private static string SafeResolve(TaggedString text)
        {
            return text.Resolve();
        }

        private static void CollectDeltas(ColonyDeepSnapshot previous, ColonyDeepSnapshot current, ColonyDeepSnapshot trendPrevious, List<OrcaNarrativeObservationCandidate> candidates)
        {
            if (previous == null || current == null || candidates == null)
            {
                return;
            }

            if (previous.humanEdibleNutrition > 0.5f && current.humanEdibleNutrition <= 0.1f)
            {
                candidates.Add(new OrcaNarrativeObservationCandidate
                {
                    kind = "state_negative",
                    source = "colony_observation",
                    title = "Food has run out",
                    body = "The colony's human-edible nutrition fell to " + current.humanEdibleNutrition.ToString("F1") + ". "
                        + "This is a colony observation and a survival pressure point.",
                    defName = "colony_food_empty",
                    baseImportance = 85,
                    cooldownKey = "colony:food_empty"
                });
            }

            if (current.downedColonists > previous.downedColonists)
            {
                candidates.Add(new OrcaNarrativeObservationCandidate
                {
                    kind = "state_negative",
                    source = "colony_observation",
                    title = "Colonist downed",
                    body = "Downed colonists increased from " + previous.downedColonists + " to " + current.downedColonists + ".",
                    defName = "colony_downed_colonists",
                    baseImportance = 80,
                    cooldownKey = "colony:downed_colonists"
                });
            }

            if (current.mentalStateColonists > previous.mentalStateColonists)
            {
                candidates.Add(new OrcaNarrativeObservationCandidate
                {
                    kind = "state_negative",
                    source = "colony_observation",
                    title = "Mental break pressure",
                    body = "Colonists in mental state increased from " + previous.mentalStateColonists + " to " + current.mentalStateColonists + ".",
                    defName = "colony_mental_states",
                    baseImportance = 70,
                    cooldownKey = "colony:mental_states"
                });
            }

            if (previous.averageMood >= 0.35f && current.averageMood < 0.25f)
            {
                candidates.Add(new OrcaNarrativeObservationCandidate
                {
                    kind = "state_negative",
                    source = "colony_observation",
                    title = "Colony mood collapsed",
                    body = "Average mood fell from " + previous.averageMood.ToStringPercent() + " to " + current.averageMood.ToStringPercent() + ".",
                    defName = "colony_mood_collapse",
                    baseImportance = 75,
                    cooldownKey = "colony:mood_collapse"
                });
            }

            if (current.playerWealth > previous.playerWealth * 1.25f && current.playerWealth - previous.playerWealth > 3000f)
            {
                candidates.Add(new OrcaNarrativeObservationCandidate
                {
                    kind = "generic",
                    source = "colony_observation",
                    title = "Wealth jumped",
                    body = "Player wealth rose from " + previous.playerWealth.ToString("F0") + " to " + current.playerWealth.ToString("F0") + ".",
                    defName = "colony_wealth_jump",
                    baseImportance = 55,
                    cooldownKey = "colony:wealth_jump"
                });
            }

            ColonyDeepSnapshot trend = trendPrevious ?? previous;
            if (current.humanEdibleNutrition < trend.humanEdibleNutrition - 2f && current.averageMood < 0.35f)
            {
                candidates.Add(new OrcaNarrativeObservationCandidate
                {
                    kind = "state_negative",
                    source = "colony_observation",
                    title = "Colony pressure is rising",
                    body = "Food and morale are trending downward without a single obvious incident. Food: "
                        + trend.humanEdibleNutrition.ToString("F1") + " -> " + current.humanEdibleNutrition.ToString("F1")
                        + ", mood: " + trend.averageMood.ToStringPercent() + " -> " + current.averageMood.ToStringPercent() + ".",
                    defName = "colony_slow_decline",
                    baseImportance = 65,
                    cooldownKey = "colony:slow_decline"
                });
            }

            if (current.humanEdibleNutrition > trend.humanEdibleNutrition + 3f
                || current.medicineCount > trend.medicineCount
                || current.averageMood > trend.averageMood + 0.08f
                || current.downedColonists < trend.downedColonists
                || current.mentalStateColonists < trend.mentalStateColonists)
            {
                candidates.Add(new OrcaNarrativeObservationCandidate
                {
                    kind = "recovery",
                    source = "colony_observation",
                    title = "Recovery period",
                    body = "The colony shows signs of recovery. Food: " + trend.humanEdibleNutrition.ToString("F1") + " -> " + current.humanEdibleNutrition.ToString("F1")
                        + ", medicine: " + trend.medicineCount + " -> " + current.medicineCount
                        + ", mood: " + trend.averageMood.ToStringPercent() + " -> " + current.averageMood.ToStringPercent()
                        + ", downed: " + trend.downedColonists + " -> " + current.downedColonists
                        + ", mental states: " + trend.mentalStateColonists + " -> " + current.mentalStateColonists + ".",
                    defName = "colony_recovery",
                    baseImportance = 50,
                    cooldownKey = "colony:recovery"
                });
            }
        }

        private void EvaluateAndMaybeEnqueue(List<OrcaNarrativeObservationCandidate> candidates, ColonyDeepSnapshot previous, ColonyDeepSnapshot current)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return;
            }

            OrcaNarrativeEvaluation evaluation = OrcaNarrativeEvaluator.Evaluate(candidates, previous, current, evaluationState);
            if (evaluation == null || evaluation.candidate == null)
            {
                return;
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            float chanceMultiplier = settings == null ? 1f : settings.colonyObservationSpeakChanceMultiplier;
            float speakChance = Mathf.Clamp01(evaluation.speakChance * chanceMultiplier);

            LogDebug("Narrative evaluation selected: classification=" + evaluation.classification
                + ", score=" + evaluation.score.ToString("F0")
                + ", chance=" + evaluation.speakChance.ToStringPercent()
                + ", adjustedChance=" + speakChance.ToStringPercent()
                + ", chanceMultiplier=" + chanceMultiplier.ToString("0.##")
                + ", theme=" + evaluation.dominantTheme
                + ", candidates=" + candidates.Count
                + ", reasons=" + string.Join("; ", evaluation.reasons.ToArray()));

            bool spoke = false;
            if (speakChance > 0f)
            {
                spoke = OrcaNarrativeDirector.EnqueueAmbientBeat(evaluation.candidate.ToBeat(evaluation), speakChance);
            }

            evaluationState.RecordResult(evaluation, spoke);
        }

        private static void LogDebug(string message)
        {
            if (DeepseekTheOrcaMod.Settings != null && DeepseekTheOrcaMod.Settings.debugLogging)
            {
                Log.Message("[RimAgent] " + message);
            }
        }
    }

    internal static class OrcaEnumerableExtensions
    {
        public static IEnumerable<T> TakeLastCompat<T>(this IEnumerable<T> source, int count)
        {
            Queue<T> queue = new Queue<T>();
            foreach (T item in source)
            {
                queue.Enqueue(item);
                while (queue.Count > count)
                {
                    queue.Dequeue();
                }
            }

            return queue;
        }
    }
}
