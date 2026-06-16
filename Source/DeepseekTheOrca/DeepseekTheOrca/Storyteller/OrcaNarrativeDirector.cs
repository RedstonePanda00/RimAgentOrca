using System.Collections.Generic;
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
}
