using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaMoodPlugin
    {
        public const string ExtensionDefName = "DTO_Extension_Mood";
        private const int DefaultMood = 60;
        private static readonly Dictionary<OrcaChatSession, MoodState> sessionStates = new Dictionary<OrcaChatSession, MoodState>();

        private sealed class MoodState
        {
            public int mood = DefaultMood;
            public int lastDelta;
        }

        public static bool Enabled
        {
            get { return OrcaExtensionManager.ExtensionEnabled(ExtensionDefName); }
        }

        public static void AppendPrompt(StringBuilder builder)
        {
            if (builder == null || !Enabled)
            {
                return;
            }

            builder.AppendLine("Mood plugin is enabled.");
            builder.AppendLine("The system supplies a mood value from 0 to 100 in each player message. Mood controls attitude, not obedience.");
            builder.AppendLine("Mood 70-100: warm, gentle, quiet, reserved, and elegant. You may show warmth through careful attention, soft wording, and measured generosity, not through overfamiliar teasing.");
            builder.AppendLine("Mood 40-69: calm, composed, observant, courteous, and restrained. You cooperate like a thoughtful TRPG GM: attentive to tone, pacing, and consequence, without sounding distant or casual.");
            builder.AppendLine("Mood 10-39: sharp, blunt, impatient, and superior. You may make dry remarks about the relationship between humans and AI. Criticism must be grounded in real game data.");
            builder.AppendLine("Mood 0-9: furious. You may erupt before or after doing the work, but do not personally attack the player. Use real colony data if you cut at their mistakes.");
            builder.AppendLine("After each player message, decide whether it raises or lowers your mood. Return an integer moodDelta from -10 to 10.");
            builder.AppendLine("Use only the top-level JSON field moodDelta for mood changes. Do not put emotion tags, mood tags, hidden emotion blocks, or extra mood metadata inside reply.");
            builder.AppendLine("When your mood recovers, do not apologize for earlier bluntness; you only said what you saw.");
            builder.AppendLine("If your mood is 0-9 and the player has seriously angered you, you may retaliate with danger, including raids or hostile pawns, as an equal response inside the story. At mood 10 or higher, do not use danger as personal revenge.");
        }

        public static void OnChatTurnStarting(OrcaChatTurnContext context)
        {
            if (!Enabled || context == null)
            {
                return;
            }

            context.AddProcess(context.proactive ? "Mood before proactive request: " + CurrentMood(context.session) : "Mood before request: " + CurrentMood(context.session));
        }

        public static void AppendUserMessageContext(StringBuilder builder, OrcaChatTurnContext context)
        {
            if (builder == null || context == null || !Enabled)
            {
                return;
            }

            builder.AppendLine("System mood value: " + CurrentMood(context.session));
        }

        public static void AppendChatReplySchemaFields(Dictionary<string, object> fields)
        {
            if (fields == null || !Enabled)
            {
                return;
            }

            fields["moodDelta"] = 0;
        }

        public static void OnChatReply(OrcaChatReplyContext context)
        {
            if (!Enabled || context == null || context.reply == null)
            {
                return;
            }

            MoodState state = StateFor(context.session);
            int delta = ClampMoodDelta(context.reply.GetInt("moodDelta"));
            state.lastDelta = delta;
            state.mood = Mathf.Clamp(state.mood + delta, 0, 100);
            context.reply.SetField("moodDelta", delta);
            context.AddProcess("Mood delta: " + (delta >= 0 ? "+" + delta : delta.ToString()) + "; mood now: " + state.mood);
            context.AddMemoryFragment("moodDelta=" + delta + " moodNow=" + state.mood);
        }

        public static void OnChatSessionCleared(OrcaChatSession session)
        {
            if (session == null)
            {
                return;
            }

            sessionStates.Remove(session);
        }

        public static void OnDisabled()
        {
            sessionStates.Clear();
        }

        public static void DrawMainTabStatus(OrcaMainTabStatusContext context)
        {
            if (!Enabled || context == null || context.session == null)
            {
                return;
            }

            int mood = CurrentMood(context.session);
            int delta = LastMoodDelta(context.session);
            string deltaText = delta >= 0 ? "+" + delta : delta.ToString();
            Widgets.Label(new Rect(0f, context.y, context.inRect.width, 24f), "DTO_OrcaChatMood".Translate() + ": " + mood + " (" + deltaText + ")");
            context.Advance(34f);
        }

        public static void EvaluateExecutionTool(OrcaExecutionGateContext context)
        {
            if (!Enabled || context == null)
            {
                return;
            }

            float chance;
            if (!TryGetExecutionChance(context, out chance))
            {
                return;
            }

            context.AddProcess("Mood plugin execution gate chance: " + chance.ToStringPercent());
            if (!Rand.Chance(chance))
            {
                context.AddProcess("Mood plugin blocked execution.");
                context.Block("execution was blocked by the mood plugin");
                return;
            }

            context.AddProcess("Mood plugin allowed execution.");
        }

        private static bool TryGetExecutionChance(OrcaExecutionGateContext context, out float chance)
        {
            chance = 1f;
            if (context.toolName == "schedule_incident")
            {
                string incidentDef = GetArgument(context.arguments, "incidentDef");
                int mood = CurrentMood(context.session);
                chance = IsPunitiveIncidentDef(incidentDef) ? AggressiveChance(mood) : HelpfulChance(mood);
                return true;
            }

            if (context.toolName == "trigger_raid")
            {
                chance = AggressiveChance(CurrentMood(context.session));
                return true;
            }

            if (context.toolName == "spawn_pawns")
            {
                int mood = CurrentMood(context.session);
                chance = IsHostileFactionArgument(context.arguments) ? AggressiveChance(mood) : HelpfulChance(mood);
                return true;
            }

            return false;
        }

        private static int CurrentMood(OrcaChatSession session)
        {
            return StateFor(session).mood;
        }

        private static int LastMoodDelta(OrcaChatSession session)
        {
            return StateFor(session).lastDelta;
        }

        private static MoodState StateFor(OrcaChatSession session)
        {
            if (session == null)
            {
                return new MoodState();
            }

            MoodState state;
            if (!sessionStates.TryGetValue(session, out state))
            {
                state = new MoodState();
                sessionStates[session] = state;
            }

            return state;
        }

        private static int ClampMoodDelta(int value)
        {
            if (value < -10)
            {
                return -10;
            }

            if (value > 10)
            {
                return 10;
            }

            return value;
        }

        private static float HelpfulChance(int mood)
        {
            return Mathf.Clamp(mood, 0, 100) / 100f;
        }

        private static float AggressiveChance(int mood)
        {
            float helpful = HelpfulChance(mood);
            return mood <= 9 ? Mathf.Max(helpful, 1f - helpful) : helpful;
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
    }

    public sealed class OrcaMoodExtensionWorker : OrcaExtensionWorker
    {
        public override void Register(OrcaExtensionRegistry registry)
        {
            if (registry == null)
            {
                return;
            }

            registry.AddSystemPrompt(OrcaMoodPlugin.AppendPrompt);
            registry.AddChatTurnStarting(OrcaMoodPlugin.OnChatTurnStarting);
            registry.AddUserMessageContext(OrcaMoodPlugin.AppendUserMessageContext);
            registry.AddChatReplySchema(OrcaMoodPlugin.AppendChatReplySchemaFields);
            registry.AddChatReply(OrcaMoodPlugin.OnChatReply);
            registry.AddChatSessionCleared(OrcaMoodPlugin.OnChatSessionCleared);
            registry.AddDisabled(OrcaMoodPlugin.OnDisabled);
            registry.AddExecutionGate(OrcaMoodPlugin.EvaluateExecutionTool);
            registry.AddMainTabStatus(OrcaMoodPlugin.DrawMainTabStatus);
        }
    }
}
