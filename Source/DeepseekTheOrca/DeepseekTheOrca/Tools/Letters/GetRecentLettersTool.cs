using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class GetLatestLetterTool : OrcaToolWorker
    {
        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            int maxChars = ParseBoundedInt(arguments, "maxChars", 700, 120, 1600);
            Letter letter = LatestArchivedLetter() ?? LatestVisibleLetter();
            if (letter == null)
            {
                return AiToolResult.Ok("no recent letter").WithValue("letter", "");
            }

            return AiToolResult.Ok("latest letter captured")
                .WithValue("letter", FormatLetter(letter, maxChars));
        }

        private static Letter LatestArchivedLetter()
        {
            if (Find.Archive == null || Find.Archive.ArchivablesListForReading == null)
            {
                return null;
            }

            return Find.Archive.ArchivablesListForReading
                .OfType<Letter>()
                .OrderByDescending(letter => letter.arrivalTick)
                .FirstOrDefault();
        }

        private static Letter LatestVisibleLetter()
        {
            if (Find.LetterStack == null || Find.LetterStack.LettersListForReading == null || Find.LetterStack.LettersListForReading.Count == 0)
            {
                return null;
            }

            return Find.LetterStack.LettersListForReading
                .OrderByDescending(letter => letter.arrivalTick)
                .FirstOrDefault();
        }

        private static string FormatLetter(Letter letter, int maxChars)
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

            if (!text.NullOrEmpty() && text.Length > maxChars)
            {
                text = text.Substring(0, maxChars) + "...";
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
