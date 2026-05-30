using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Verse;
namespace DeepseekTheOrca
{
    public static partial class OrcaSkillManager
    {
        private static void AppendReferenceSnippets(StringBuilder builder, OrcaSkillProfile skill, string turnText)
        {
            List<ReferenceSnippet> snippets = ReferenceSnippetsFor(skill, turnText);
            if (snippets.Count == 0)
            {
                return;
            }

            int totalChars = 0;
            builder.AppendLine("Relevant references:");
            for (int i = 0; i < snippets.Count; i++)
            {
                ReferenceSnippet snippet = snippets[i];
                if (snippet == null || snippet.text.NullOrEmpty())
                {
                    continue;
                }

                string text = snippet.text;
                int remaining = MaxReferenceTotalCharsPerSkill - totalChars;
                if (remaining <= 0)
                {
                    break;
                }
                if (text.Length > remaining)
                {
                    text = text.Substring(0, remaining).TrimEnd() + "...";
                }

                builder.AppendLine("- " + SafeLine(snippet.relativePath) + ":");
                builder.AppendLine(text);
                totalChars += text.Length;
            }
        }

        private static List<ReferenceSnippet> ReferenceSnippetsFor(OrcaSkillProfile skill, string turnText)
        {
            List<ReferenceSnippet> result = new List<ReferenceSnippet>();
            if (skill == null || skill.folderPath.NullOrEmpty() || turnText.NullOrEmpty())
            {
                return result;
            }

            string referenceRoot = Path.Combine(skill.folderPath, "references");
            if (!Directory.Exists(referenceRoot))
            {
                return result;
            }

            HashSet<string> terms = ReferenceQueryTerms(skill, turnText);
            if (terms.Count == 0)
            {
                return result;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.GetFiles(referenceRoot, "*", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Log.Warning("[RimAgent] Failed to scan skill references " + referenceRoot + ": " + ex.Message);
                return result;
            }

            foreach (string file in files)
            {
                ReferenceSnippet snippet = ReferenceSnippetForFile(referenceRoot, file, terms);
                if (snippet != null && snippet.score > 0 && !snippet.text.NullOrEmpty())
                {
                    result.Add(snippet);
                }
            }

            return result.OrderByDescending(snippet => snippet.score)
                .ThenBy(snippet => snippet.relativePath)
                .Take(MaxReferenceSnippetsPerSkill)
                .ToList();
        }

        private static ReferenceSnippet ReferenceSnippetForFile(string referenceRoot, string file, HashSet<string> terms)
        {
            if (file.NullOrEmpty() || terms == null || terms.Count == 0)
            {
                return null;
            }

            string extension = Path.GetExtension(file);
            if (!ReferenceExtensions.Contains(extension))
            {
                return null;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(file);
            }
            catch
            {
                return null;
            }

            if (!info.Exists || info.Length <= 0 || info.Length > MaxReferenceFileBytes)
            {
                return null;
            }

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                Log.Warning("[RimAgent] Failed to read skill reference " + file + ": " + ex.Message);
                return null;
            }

            if (text.NullOrEmpty())
            {
                return null;
            }

            string relativePath = RelativeReferencePath(referenceRoot, file);
            string lowerPath = relativePath.ToLowerInvariant();
            string lowerText = text.ToLowerInvariant();
            int score = 0;
            foreach (string term in terms)
            {
                if (lowerPath.Contains(term))
                {
                    score += 8;
                }
                int count = CountOccurrences(lowerText, term);
                score += Math.Min(count, 8);
            }

            if (score <= 0)
            {
                return null;
            }

            return new ReferenceSnippet
            {
                relativePath = relativePath,
                text = BestReferenceSnippet(text, terms),
                score = score
            };
        }

        private static string BestReferenceSnippet(string text, HashSet<string> terms)
        {
            string normalized = NormalizeReferenceText(text);
            if (normalized.Length <= MaxReferenceSnippetChars)
            {
                return normalized;
            }

            string lower = normalized.ToLowerInvariant();
            int bestIndex = -1;
            foreach (string term in terms)
            {
                int index = lower.IndexOf(term, StringComparison.Ordinal);
                if (index >= 0 && (bestIndex < 0 || index < bestIndex))
                {
                    bestIndex = index;
                }
            }

            if (bestIndex < 0)
            {
                bestIndex = 0;
            }

            int start = Math.Max(0, bestIndex - MaxReferenceSnippetChars / 3);
            int end = Math.Min(normalized.Length, start + MaxReferenceSnippetChars);
            start = MoveToLineBoundary(normalized, start, searchBackward: true);
            end = MoveToLineBoundary(normalized, end, searchBackward: false);
            string snippet = normalized.Substring(start, end - start).Trim();
            if (start > 0)
            {
                snippet = "..." + snippet;
            }
            if (end < normalized.Length)
            {
                snippet += "...";
            }

            return snippet;
        }

        private static string NormalizeReferenceText(string text)
        {
            text = text ?? "";
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.None)
                .Select(line => line.TrimEnd())
                .ToArray();
            return string.Join("\n", lines).Trim();
        }

        private static int MoveToLineBoundary(string text, int index, bool searchBackward)
        {
            if (text.NullOrEmpty())
            {
                return 0;
            }

            index = Math.Max(0, Math.Min(index, text.Length));
            if (searchBackward)
            {
                int line = text.LastIndexOf('\n', Math.Max(0, index - 1));
                return line < 0 ? 0 : line + 1;
            }

            int next = text.IndexOf('\n', index);
            return next < 0 ? text.Length : next;
        }

        private static HashSet<string> ReferenceQueryTerms(OrcaSkillProfile skill, string turnText)
        {
            HashSet<string> terms = new HashSet<string>();
            AddReferenceTerms(terms, turnText);
            if (skill != null)
            {
                AddReferenceTerms(terms, skill.label);
                AddReferenceTerms(terms, skill.description);
                if (skill.triggerHints != null)
                {
                    for (int i = 0; i < skill.triggerHints.Count; i++)
                    {
                        AddReferenceTerms(terms, skill.triggerHints[i]);
                    }
                }
            }

            return terms;
        }

        private static void AddReferenceTerms(HashSet<string> terms, string text)
        {
            if (terms == null || text.NullOrEmpty())
            {
                return;
            }

            foreach (Match match in Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{Nd}_-]{2,}"))
            {
                string term = match.Value.Trim('_', '-');
                if (term.Length >= 2 && !IsStopTerm(term))
                {
                    terms.Add(term);
                    AddCjkReferenceTerms(terms, term);
                }
            }
        }

        private static void AddCjkReferenceTerms(HashSet<string> terms, string term)
        {
            if (terms == null || term.NullOrEmpty() || !ContainsCjk(term))
            {
                return;
            }

            StringBuilder cjk = new StringBuilder();
            for (int i = 0; i < term.Length; i++)
            {
                char ch = term[i];
                if (IsCjk(ch))
                {
                    cjk.Append(ch);
                }
            }

            string text = cjk.ToString();
            for (int length = 2; length <= 3; length++)
            {
                if (text.Length < length)
                {
                    continue;
                }

                for (int i = 0; i <= text.Length - length && terms.Count < 120; i++)
                {
                    terms.Add(text.Substring(i, length));
                }
            }
        }

        private static bool ContainsCjk(string value)
        {
            if (value.NullOrEmpty())
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (IsCjk(value[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCjk(char ch)
        {
            return (ch >= 0x4E00 && ch <= 0x9FFF)
                || (ch >= 0x3400 && ch <= 0x4DBF)
                || (ch >= 0xF900 && ch <= 0xFAFF);
        }

        private static bool IsStopTerm(string term)
        {
            switch (term)
            {
                case "the":
                case "and":
                case "for":
                case "with":
                case "that":
                case "this":
                case "you":
                case "your":
                case "are":
                case "was":
                case "from":
                case "into":
                case "about":
                case "when":
                case "what":
                case "where":
                case "which":
                case "skill":
                case "orca":
                    return true;
                default:
                    return false;
            }
        }

        private static int CountOccurrences(string text, string term)
        {
            if (text.NullOrEmpty() || term.NullOrEmpty())
            {
                return 0;
            }

            int count = 0;
            int index = 0;
            while (index < text.Length)
            {
                index = text.IndexOf(term, index, StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }
                count++;
                index += term.Length;
            }

            return count;
        }

        private static string RelativeReferencePath(string referenceRoot, string file)
        {
            try
            {
                Uri root = new Uri(AppendDirectorySeparator(referenceRoot));
                Uri target = new Uri(file);
                return Uri.UnescapeDataString(root.MakeRelativeUri(target).ToString()).Replace('/', Path.DirectorySeparatorChar);
            }
            catch
            {
                return Path.GetFileName(file);
            }
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (path.NullOrEmpty())
            {
                return Path.DirectorySeparatorChar.ToString();
            }

            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private sealed class SkillMarkdown
        {
            public string name = "";
            public string displayName = "";
            public string description = "";
            public bool enabled = true;
            public string activation = "auto";
            public List<string> triggerHints = new List<string>();
            public List<string> contexts = new List<string>();
            public List<string> allowedTools = new List<string>();
            public string instructions = "";
        }
    }
}
