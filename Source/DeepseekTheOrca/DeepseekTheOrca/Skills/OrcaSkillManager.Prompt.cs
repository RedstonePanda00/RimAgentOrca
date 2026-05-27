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
        public static string FormatEnabledSkillPrompt()
        {
            return FormatSkillPrompt(EnabledSkills(), "Enabled skill modules:", "", null);
        }

        public static string FormatActiveSkillPrompt(string turnText)
        {
            return FormatActiveSkillPrompt(turnText, null);
        }

        public static string FormatActiveSkillPrompt(string turnText, IEnumerable<string> contextTags)
        {
            return FormatSkillPrompt(ActiveSkillsFor(turnText, contextTags), "Active skill context for this turn:", turnText, contextTags);
        }

        public static string FormatSelectedSkillPrompt(IEnumerable<string> skillIds, string turnText)
        {
            return FormatSkillPrompt(SelectedSkillsForIds(skillIds), "Selected skill context for this turn:", turnText, null);
        }

        public static string FormatSkillSelectionCatalog()
        {
            List<OrcaSkillProfile> skills = EnabledSkills();
            if (skills.Count == 0)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Enabled skill catalog for natural-language skill selection:");
            builder.AppendLine("Select skillIds only when their description is directly relevant to the latest player request. Return only ids listed here.");
            for (int i = 0; i < skills.Count; i++)
            {
                OrcaSkillProfile skill = skills[i];
                builder.Append("- id=").Append(SafeLine(skill.id));
                builder.Append("; name=").Append(SafeLine(skill.label));
                if (!skill.description.NullOrEmpty())
                {
                    builder.Append("; description=").Append(SafeLine(skill.description));
                }
                if (skill.allowedTools != null && skill.allowedTools.Count > 0)
                {
                    builder.Append("; tools=").Append(string.Join(",", skill.allowedTools.ToArray()));
                }
                builder.AppendLine();
            }

            return builder.ToString();
        }

        public static List<string> ValidEnabledSkillIds(IEnumerable<string> skillIds)
        {
            return SelectedSkillsForIds(skillIds).Select(skill => skill.id).ToList();
        }

        public static List<OrcaSkillProfile> ActiveSkillsFor(string turnText)
        {
            return ActiveSkillsFor(turnText, null);
        }

        public static List<OrcaSkillProfile> ActiveSkillsFor(string turnText, IEnumerable<string> contextTags)
        {
            List<OrcaSkillProfile> enabled = EnabledSkills();
            List<string> tags = CleanContextTags(contextTags);
            if (turnText.NullOrEmpty() && tags.Count == 0)
            {
                return new List<OrcaSkillProfile>();
            }

            return enabled.Where(skill => SkillMatchesTurn(skill, turnText, tags)).OrderBy(skill => skill.label).ToList();
        }

        private static List<OrcaSkillProfile> SelectedSkillsForIds(IEnumerable<string> skillIds)
        {
            if (skillIds == null)
            {
                return new List<OrcaSkillProfile>();
            }

            List<string> requested = skillIds.Select(value => value == null ? "" : value.Trim())
                .Where(value => !value.NullOrEmpty())
                .Distinct()
                .ToList();
            if (requested.Count == 0)
            {
                return new List<OrcaSkillProfile>();
            }

            List<OrcaSkillProfile> enabled = EnabledSkills();
            List<OrcaSkillProfile> result = new List<OrcaSkillProfile>();
            for (int i = 0; i < requested.Count; i++)
            {
                string id = requested[i];
                OrcaSkillProfile skill = enabled.FirstOrDefault(profile => profile.id == id);
                if (skill != null && !result.Any(existing => existing.id == skill.id))
                {
                    result.Add(skill);
                }
            }

            return result;
        }

        private static string FormatSkillPrompt(List<OrcaSkillProfile> skills, string header, string turnText, IEnumerable<string> contextTags)
        {
            if (skills.Count == 0)
            {
                return "";
            }

            List<string> tags = CleanContextTags(contextTags);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(header);
            builder.AppendLine("Agent skills are lightweight capability folders with metadata and instructions. Use a skill only when it is relevant to the latest player message, proactive trigger, tool results, or game context. A skill changes how you perform that task; it is not a persona.");
            builder.AppendLine("If a skill lists allowed tools, prefer those tools while handling that skill and avoid unrelated execution tools unless the player explicitly asks or the story context clearly requires them.");
            if (tags.Count > 0)
            {
                builder.AppendLine("Current context tags: " + string.Join(", ", tags.ToArray()));
            }
            for (int i = 0; i < skills.Count; i++)
            {
                OrcaSkillProfile skill = skills[i];
                builder.AppendLine();
                builder.AppendLine("Skill: " + SafeLine(skill.label));
                if (!skill.description.NullOrEmpty())
                {
                    builder.AppendLine("Description: " + SafeLine(skill.description));
                }
                if (skill.triggerHints != null && skill.triggerHints.Count > 0)
                {
                    builder.AppendLine("Trigger hints: " + string.Join(", ", skill.triggerHints.ToArray()));
                }
                if (skill.contexts != null && skill.contexts.Count > 0)
                {
                    builder.AppendLine("Skill contexts: " + string.Join(", ", skill.contexts.ToArray()));
                }
                if (skill.allowedTools != null && skill.allowedTools.Count > 0)
                {
                    builder.AppendLine("Allowed/recommended tools: " + string.Join(", ", skill.allowedTools.ToArray()));
                }
                if (!skill.activation.NullOrEmpty() && skill.activation != "auto")
                {
                    builder.AppendLine("Activation: " + SafeLine(skill.activation));
                }
                if (!skill.prompt.NullOrEmpty())
                {
                    builder.AppendLine("Instructions:");
                    builder.AppendLine(skill.prompt.Trim());
                }
                if (!skill.folderPath.NullOrEmpty())
                {
                    builder.AppendLine("Skill folder: " + SafeLine(skill.folderPath));
                }
                AppendReferenceSnippets(builder, skill, turnText);
            }

            return builder.ToString().TrimEnd();
        }

        public static string FormatControllerRoutingHint()
        {
            List<OrcaSkillProfile> skills = EnabledSkills();
            if (skills.Count == 0)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("Enabled skill modules may affect routing. If the latest turn matches a skill trigger, or the skill would need listed game/MCP/web tools, choose the appropriate specialist route. Skills: ");
            for (int i = 0; i < skills.Count; i++)
            {
                OrcaSkillProfile skill = skills[i];
                if (i > 0)
                {
                    builder.Append("; ");
                }
                builder.Append(SafeLine(skill.label));
                if (skill.triggerHints != null && skill.triggerHints.Count > 0)
                {
                    builder.Append(" triggers=");
                    builder.Append(string.Join(",", skill.triggerHints.ToArray()));
                }
                if (skill.contexts != null && skill.contexts.Count > 0)
                {
                    builder.Append(" contexts=");
                    builder.Append(string.Join(",", skill.contexts.ToArray()));
                }
                if (skill.allowedTools != null && skill.allowedTools.Count > 0)
                {
                    builder.Append(" tools=");
                    builder.Append(string.Join(",", skill.allowedTools.ToArray()));
                }
                if (!skill.activation.NullOrEmpty() && skill.activation != "auto")
                {
                    builder.Append(" activation=");
                    builder.Append(SafeLine(skill.activation));
                }
            }

            return builder.ToString();
        }
        private static bool SkillMatchesTurn(OrcaSkillProfile skill, string turnText, List<string> contextTags)
        {
            if (skill == null)
            {
                return false;
            }

            if (skill.contexts != null && skill.contexts.Count > 0 && contextTags != null && contextTags.Count > 0)
            {
                for (int i = 0; i < skill.contexts.Count; i++)
                {
                    if (contextTags.Contains(NormalizeContextTag(skill.contexts[i])))
                    {
                        return true;
                    }
                }
            }

            if (turnText.NullOrEmpty())
            {
                return false;
            }

            string text = turnText.ToLowerInvariant();
            if (TextContains(text, skill.label) || TextContains(text, skill.description))
            {
                return true;
            }

            if (skill.triggerHints != null && skill.triggerHints.Any(value => TextContains(text, value)))
            {
                return true;
            }

            return false;
        }

        private static string NormalizeContextTag(string value)
        {
            value = value == null ? "" : value.Trim().ToLowerInvariant();
            if (value.NullOrEmpty())
            {
                return "";
            }

            value = Regex.Replace(value, "[^a-z0-9_.:-]+", "_").Trim('_');
            return value;
        }

        private static bool TextContains(string haystackLower, string value)
        {
            if (haystackLower.NullOrEmpty() || value.NullOrEmpty())
            {
                return false;
            }

            return haystackLower.Contains(value.Trim().ToLowerInvariant());
        }

        private static string SafeLine(string value)
        {
            return (value ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string NormalizeActivation(string activation)
        {
            return "auto";
        }
    }
}
