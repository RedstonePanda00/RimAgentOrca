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
            return FormatSkillPrompt(EnabledSkills(), "Enabled skill modules:", "", null, OrcaLlmModelRole.Fallback);
        }

        public static string FormatActiveSkillPrompt(string turnText)
        {
            return FormatActiveSkillPrompt(turnText, null);
        }

        public static string FormatActiveSkillPrompt(string turnText, IEnumerable<string> contextTags)
        {
            return FormatSkillPrompt(ActiveSkillsFor(turnText, contextTags), "Active skill context for this turn:", turnText, contextTags, OrcaLlmModelRole.Fallback);
        }

        public static string FormatSelectedSkillPrompt(IEnumerable<string> skillIds, string turnText)
        {
            return FormatSelectedSkillPrompt(skillIds, turnText, OrcaLlmModelRole.Fallback);
        }

        public static string FormatSelectedSkillPrompt(IEnumerable<string> skillIds, string turnText, OrcaLlmModelRole role)
        {
            return FormatSkillPrompt(SelectedSkillsForIds(skillIds), SelectedSkillHeader(role), turnText, null, role);
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

        public static HashSet<string> AllowedToolsForSkillIds(IEnumerable<string> skillIds)
        {
            HashSet<string> result = new HashSet<string>();
            List<OrcaSkillProfile> skills = SelectedSkillsForIds(skillIds);
            for (int i = 0; i < skills.Count; i++)
            {
                OrcaSkillProfile skill = skills[i];
                foreach (string toolName in skill.allowedTools ?? new List<string>())
                {
                    if (!toolName.NullOrEmpty())
                    {
                        result.Add(toolName.Trim());
                    }
                }
            }

            return result;
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

        private static string FormatSkillPrompt(List<OrcaSkillProfile> skills, string header, string turnText, IEnumerable<string> contextTags, OrcaLlmModelRole role)
        {
            if (skills.Count == 0)
            {
                return "";
            }

            List<string> tags = CleanContextTags(contextTags);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(header);
            builder.AppendLine("Agent skills are lightweight capability folders with metadata and instructions. Use a skill only when it is relevant to the latest player message, proactive trigger, tool results, or game context. A skill changes how you perform that task; it is not a persona.");
            if (role == OrcaLlmModelRole.Dialogue)
            {
                builder.AppendLine("This is the final dialogue stage. Skill instructions affect player-facing wording and interpretation only; do not request, call, or describe tool calls.");
            }
            else
            {
                builder.AppendLine("If a skill lists allowed tools, prefer those tools while handling that skill and avoid unrelated execution tools unless the player explicitly asks or the story context clearly requires them.");
            }
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
                if (role != OrcaLlmModelRole.Dialogue && skill.allowedTools != null && skill.allowedTools.Count > 0)
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
                    builder.AppendLine(RoleSpecificSkillPrompt(skill.prompt, role));
                }
                if (!skill.folderPath.NullOrEmpty())
                {
                    builder.AppendLine("Skill folder: " + SafeLine(skill.folderPath));
                }
                AppendReferenceSnippets(builder, skill, turnText);
            }

            return builder.ToString().TrimEnd();
        }

        private static string SelectedSkillHeader(OrcaLlmModelRole role)
        {
            return role == OrcaLlmModelRole.Dialogue
                ? "Selected skill style context for final dialogue:"
                : "Selected skill context for this turn:";
        }

        private static string RoleSpecificSkillPrompt(string prompt, OrcaLlmModelRole role)
        {
            if (prompt.NullOrEmpty() || role != OrcaLlmModelRole.Dialogue)
            {
                return prompt == null ? "" : prompt.Trim();
            }

            string[] paragraphs = Regex.Split(prompt.Trim(), @"\r?\n\s*\r?\n");
            List<string> kept = new List<string>();
            for (int i = 0; i < paragraphs.Length; i++)
            {
                string paragraph = paragraphs[i].Trim();
                if (paragraph.NullOrEmpty() || IsToolExecutionInstruction(paragraph))
                {
                    continue;
                }

                kept.Add(paragraph);
            }

            return string.Join("\n\n", kept.ToArray()).Trim();
        }

        private static bool IsToolExecutionInstruction(string paragraph)
        {
            string lower = (paragraph ?? "").ToLowerInvariant();
            return lower.Contains("allowed/recommended tools")
                || lower.Contains("may call execution tool")
                || lower.Contains("call execution tool")
                || lower.Contains("request tool")
                || lower.Contains("request or call")
                || lower.Contains("execution tools on their own initiative")
                || lower.Contains("do not use execution tools")
                || lower.Contains("before any event, raid, or pawn spawn is executed")
                || lower.Contains("if an execution tool")
                || lower.Contains("execution tool succeeds")
                || lower.Contains("execution tool fails");
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
