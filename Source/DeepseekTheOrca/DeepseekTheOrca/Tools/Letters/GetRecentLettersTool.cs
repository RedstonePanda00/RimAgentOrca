using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class GetRecentLettersTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "get_recent_letters"; }
        }

        public string Description
        {
            get { return "Read recent letters from the game archive, falling back to the visible LetterStack."; }
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            int count = ParseCount(arguments);
            List<Letter> letters = RecentArchivedLetters(count);
            if (letters.Count == 0)
            {
                letters = RecentVisibleLetters(count);
            }

            if (letters.Count == 0)
            {
                return AiToolResult.Ok("no recent letters").WithValue("letters", "");
            }

            List<string> summaries = new List<string>();
            for (int i = letters.Count - 1; i >= 0; i--)
            {
                summaries.Add(FormatLetter(letters[i]));
            }

            return AiToolResult.Ok("recent letter count: " + summaries.Count)
                .WithValue("letters", string.Join(" || ", summaries.ToArray()));
        }

        private static int ParseCount(Dictionary<string, string> arguments)
        {
            int count = 5;
            string countText;
            if (arguments.TryGetValue("count", out countText))
            {
                int.TryParse(countText, out count);
            }
            return Mathf.Clamp(count <= 0 ? 5 : count, 1, 10);
        }

        private static List<Letter> RecentArchivedLetters(int count)
        {
            if (Find.Archive == null || Find.Archive.ArchivablesListForReading == null)
            {
                return new List<Letter>();
            }

            List<Letter> archivedLetters = Find.Archive.ArchivablesListForReading
                .OfType<Letter>()
                .OrderBy(letter => letter.arrivalTick)
                .ToList();

            if (archivedLetters.Count <= count)
            {
                return archivedLetters;
            }

            return archivedLetters.GetRange(archivedLetters.Count - count, count);
        }

        private static List<Letter> RecentVisibleLetters(int count)
        {
            if (Find.LetterStack == null || Find.LetterStack.LettersListForReading == null)
            {
                return new List<Letter>();
            }

            List<Letter> visibleLetters = Find.LetterStack.LettersListForReading;
            int start = Mathf.Max(0, visibleLetters.Count - count);
            return visibleLetters.GetRange(start, visibleLetters.Count - start);
        }

        private static string FormatLetter(Letter letter)
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

            if (!text.NullOrEmpty() && text.Length > 500)
            {
                text = text.Substring(0, 500) + "...";
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
    }
}
