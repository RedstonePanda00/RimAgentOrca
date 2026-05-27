using System.Text;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaMoodPlugin
    {
        public const string ExtensionDefName = "DTO_Extension_Mood";

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

        public static bool TryDrawMainTabStatus(Rect inRect, ref float y, OrcaChatSession session)
        {
            if (!Enabled || session == null)
            {
                return false;
            }

            int mood = session.Mood;
            int delta = session.LastMoodDelta;
            string deltaText = delta >= 0 ? "+" + delta : delta.ToString();
            Widgets.Label(new Rect(0f, y, inRect.width, 24f), "DTO_OrcaChatMood".Translate() + ": " + mood + " (" + deltaText + ")");
            y += 34f;
            return true;
        }
    }

    public sealed class OrcaMoodExtensionWorker : OrcaExtensionWorker
    {
        public override void AppendSystemPrompt(StringBuilder builder)
        {
            OrcaMoodPlugin.AppendPrompt(builder);
        }
    }
}
