using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaSkillProfile
    {
        public string id;
        public string skillName;
        public string displayName;
        public string label;
        public string description;
        public bool enabled;
        public List<string> triggerHints = new List<string>();
        public string prompt;
        public List<string> allowedTools = new List<string>();
        public bool readOnly;
        public string filePath;
        public string folderPath;
        public string sourceMod;
        public bool defaultEnabled = true;
        public string format = "skill.md";
        public string activation = "auto";

        public bool IsLocal
        {
            get { return id != null && id.StartsWith(OrcaSkillManager.LocalPrefix, StringComparison.Ordinal); }
        }
    }

    public static class OrcaSkillManager
    {
        public const string LocalPrefix = "local:";
        public const string SkillPrefix = "skill:";
        private const int MaxReferenceFileBytes = 65536;
        private const int MaxReferenceSnippetsPerSkill = 3;
        private const int MaxReferenceSnippetChars = 900;
        private const int MaxReferenceTotalCharsPerSkill = 2400;
        private static readonly List<OrcaSkillProfile> localSkills = new List<OrcaSkillProfile>();
        private static bool loadedLocal;
        private static readonly HashSet<string> ReferenceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".md",
            ".markdown",
            ".txt",
            ".json",
            ".xml",
            ".yaml",
            ".yml",
            ".csv"
        };

        public static string SkillFolderPath
        {
            get { return Path.Combine(GenFilePaths.ConfigFolderPath, "DeepseekTheOrca", "Skills"); }
        }

        public static List<OrcaSkillProfile> AllSkills()
        {
            EnsureLoaded();
            List<OrcaSkillProfile> result = new List<OrcaSkillProfile>();
            result.AddRange(localSkills);
            result.AddRange(ModFolderSkills());
            return result.OrderBy(profile => profile.label).ToList();
        }

        public static List<OrcaSkillProfile> EnabledSkills()
        {
            return AllSkills().Where(profile => profile.enabled).OrderBy(profile => profile.label).ToList();
        }

        public static OrcaSkillProfile CreateLocal()
        {
            EnsureLoaded();
            string skillName = Guid.NewGuid().ToString("N");
            OrcaSkillProfile profile = new OrcaSkillProfile
            {
                id = LocalPrefix + skillName,
                skillName = skillName,
                displayName = "New Skill",
                label = "New Skill",
                description = "Describe when this skill should be used.",
                enabled = true,
                triggerHints = new List<string> { "keyword" },
                prompt = "Write the skill instructions here. Keep this focused on the skill's domain-specific behavior.",
                allowedTools = new List<string>(),
                readOnly = false,
                defaultEnabled = true,
                format = "skill.md",
                activation = "auto"
            };
            profile.folderPath = FolderPathFor(profile);
            profile.filePath = Path.Combine(profile.folderPath, "SKILL.md");
            localSkills.Add(profile);
            Save(profile);
            OrcaChatWindowManager.Session.Clear();
            return profile;
        }

        public static void Save(OrcaSkillProfile profile)
        {
            if (profile == null || profile.readOnly)
            {
                return;
            }

            EnsureDirectory();
            if (profile.id.NullOrEmpty() || !profile.id.StartsWith(LocalPrefix, StringComparison.Ordinal))
            {
                profile.id = LocalPrefix + Guid.NewGuid().ToString("N");
            }

            Normalize(profile);

            SaveSkillMarkdown(profile);
        }

        public static void Delete(OrcaSkillProfile profile)
        {
            if (profile == null || profile.readOnly)
            {
                return;
            }

            EnsureLoaded();
            localSkills.RemoveAll(item => item.id == profile.id);
            if (!profile.folderPath.NullOrEmpty() && Directory.Exists(profile.folderPath))
            {
                Directory.Delete(profile.folderPath, true);
            }

            OrcaChatWindowManager.Session.Clear();
        }

        public static void SetEnabled(OrcaSkillProfile profile, bool enabled)
        {
            if (profile == null)
            {
                return;
            }

            if (profile.readOnly)
            {
                DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
                if (settings != null)
                {
                    settings.SetExternalSkillEnabled(profile.id, enabled, profile.defaultEnabled);
                }
            }
            else
            {
                profile.enabled = enabled;
                Save(profile);
            }

            OrcaChatWindowManager.Session.Clear();
        }

        public static void ReloadLocal()
        {
            loadedLocal = false;
            localSkills.Clear();
            EnsureLoaded();
            OrcaChatWindowManager.Session.Clear();
        }

        public static string FormatEnabledSkillPrompt()
        {
            return FormatSkillPrompt(EnabledSkills(), "Enabled skill modules:", "");
        }

        public static string FormatActiveSkillPrompt(string turnText)
        {
            return FormatSkillPrompt(ActiveSkillsFor(turnText), "Active skill context for this turn:", turnText);
        }

        public static List<OrcaSkillProfile> ActiveSkillsFor(string turnText)
        {
            List<OrcaSkillProfile> enabled = EnabledSkills();
            if (turnText.NullOrEmpty())
            {
                return new List<OrcaSkillProfile>();
            }

            return enabled.Where(skill => SkillMatchesTurn(skill, turnText)).OrderBy(skill => skill.label).ToList();
        }

        private static string FormatSkillPrompt(List<OrcaSkillProfile> skills, string header, string turnText)
        {
            if (skills.Count == 0)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine(header);
            builder.AppendLine("Agent skills are lightweight capability folders with metadata and instructions. Use a skill only when it is relevant to the latest player message, proactive trigger, tool results, or game context. A skill changes how you perform that task; it is not a persona.");
            builder.AppendLine("If a skill lists allowed tools, prefer those tools while handling that skill and avoid unrelated execution tools unless the player explicitly asks or the story context clearly requires them.");
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

        private static void EnsureLoaded()
        {
            if (loadedLocal)
            {
                return;
            }

            loadedLocal = true;
            localSkills.Clear();
            EnsureDirectory();
            foreach (string directory in Directory.GetDirectories(SkillFolderPath))
            {
                OrcaSkillProfile profile = LoadSkillFolder(directory, readOnly: false, sourceMod: "");
                if (profile != null)
                {
                    localSkills.Add(profile);
                }
            }
        }

        private static List<OrcaSkillProfile> ModFolderSkills()
        {
            List<OrcaSkillProfile> result = new List<OrcaSkillProfile>();
            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
            if (mods == null)
            {
                return result;
            }

            for (int i = 0; i < mods.Count; i++)
            {
                ModContentPack mod = mods[i];
                if (mod == null || mod.RootDir.NullOrEmpty())
                {
                    continue;
                }

                LoadModSkillRoot(result, mod, Path.Combine(mod.RootDir, "OrcaSkills"));
                LoadModSkillRoot(result, mod, Path.Combine(mod.RootDir, "Skills"));
            }

            return result;
        }

        private static void LoadModSkillRoot(List<OrcaSkillProfile> result, ModContentPack mod, string root)
        {
            if (result == null || mod == null || root.NullOrEmpty() || !Directory.Exists(root))
            {
                return;
            }

            foreach (string directory in Directory.GetDirectories(root))
            {
                OrcaSkillProfile profile = LoadSkillFolder(directory, readOnly: true, sourceMod: SourceNameFor(mod));
                if (profile != null)
                {
                    string packageId = mod.PackageIdPlayerFacing.NullOrEmpty() ? mod.PackageId : mod.PackageIdPlayerFacing;
                    profile.id = SkillPrefix + packageId + ":" + Path.GetFileName(directory);
                    DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
                    profile.enabled = settings == null ? profile.defaultEnabled : settings.IsExternalSkillEnabled(profile.id, profile.defaultEnabled);
                    result.Add(profile);
                }
            }
        }

        private static void Normalize(OrcaSkillProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            if (profile.label.NullOrEmpty())
            {
                profile.label = profile.displayName.NullOrEmpty() ? profile.skillName : profile.displayName;
            }
            profile.displayName = profile.displayName.NullOrEmpty() ? profile.label : profile.displayName;
            profile.skillName = NormalizeSkillName(profile.skillName.NullOrEmpty() ? FolderNameFor(profile) : profile.skillName);
            profile.description = profile.description ?? "";
            profile.prompt = profile.prompt ?? "";
            profile.triggerHints = CleanList(profile.triggerHints);
            profile.allowedTools = CleanList(profile.allowedTools);
            if (profile.format.NullOrEmpty())
            {
                profile.format = "skill.md";
            }
            profile.activation = NormalizeActivation(profile.activation);
        }

        private static List<string> CleanList(List<string> values)
        {
            if (values == null)
            {
                return new List<string>();
            }

            return values.Select(value => value == null ? "" : value.Trim())
                .Where(value => !value.NullOrEmpty())
                .Distinct()
                .ToList();
        }

        private static void EnsureDirectory()
        {
            Directory.CreateDirectory(SkillFolderPath);
        }

        private static string FolderPathFor(OrcaSkillProfile profile)
        {
            string name = FolderNameFor(profile);
            return Path.Combine(SkillFolderPath, name);
        }

        private static string FolderNameFor(OrcaSkillProfile profile)
        {
            string name = profile == null || profile.id == null ? Guid.NewGuid().ToString("N") : profile.id.Replace(LocalPrefix, "");
            return Regex.Replace(name, "[^A-Za-z0-9_.-]", "_");
        }

        private static bool IsValidSkillName(string name)
        {
            return !name.NullOrEmpty() && Regex.IsMatch(name, "^[a-z0-9]+(?:-[a-z0-9]+)*$");
        }

        private static string NormalizeSkillName(string name)
        {
            name = name == null ? "" : name.Trim().ToLowerInvariant();
            name = Regex.Replace(name, "[^a-z0-9]+", "-").Trim('-');
            return name.NullOrEmpty() ? Guid.NewGuid().ToString("N") : name;
        }

        private static OrcaSkillProfile LoadSkillFolder(string directory, bool readOnly, string sourceMod)
        {
            string skillFile = Path.Combine(directory, "SKILL.md");
            if (!File.Exists(skillFile))
            {
                return null;
            }

            try
            {
                string folderName = Path.GetFileName(directory);
                SkillMarkdown markdown = ParseSkillMarkdown(File.ReadAllText(skillFile), folderName, skillFile);
                string skillName = markdown.name.NullOrEmpty() ? folderName : markdown.name;
                if (markdown.name.NullOrEmpty())
                {
                    Log.Warning("[Deepseek The Orca] Skill " + directory + " has no metadata name; using folder name '" + folderName + "'.");
                }
                else if (!IsValidSkillName(markdown.name))
                {
                    Log.Warning("[Deepseek The Orca] Skill " + directory + " has invalid name '" + markdown.name + "'. Skill names should use lowercase letters, numbers, and hyphens; using folder name '" + folderName + "'.");
                    skillName = folderName;
                }
                if (!string.Equals(skillName, folderName, StringComparison.Ordinal))
                {
                    Log.Warning("[Deepseek The Orca] Skill name '" + skillName + "' should match folder name '" + folderName + "'.");
                }
                if (markdown.description.NullOrEmpty())
                {
                    Log.Warning("[Deepseek The Orca] Skill " + directory + " has no metadata description.");
                }
                OrcaSkillProfile profile = new OrcaSkillProfile
                {
                    id = readOnly ? SkillPrefix + folderName : LocalPrefix + folderName,
                    skillName = skillName,
                    displayName = markdown.displayName,
                    label = markdown.displayName.NullOrEmpty() ? skillName : markdown.displayName,
                    description = markdown.description,
                    enabled = markdown.enabled,
                    triggerHints = markdown.triggerHints,
                    prompt = markdown.instructions,
                    allowedTools = markdown.allowedTools,
                    readOnly = readOnly,
                    filePath = skillFile,
                    folderPath = directory,
                    sourceMod = sourceMod,
                    defaultEnabled = markdown.enabled,
                    format = "skill.md",
                    activation = markdown.activation
                };
                Normalize(profile);
                return profile;
            }
            catch (Exception ex)
            {
                Log.Warning("[Deepseek The Orca] Failed to load skill folder " + directory + ": " + ex.Message);
                return null;
            }
        }

        private static void SaveSkillMarkdown(OrcaSkillProfile profile)
        {
            profile.folderPath = profile.folderPath.NullOrEmpty() ? FolderPathFor(profile) : profile.folderPath;
            profile.filePath = Path.Combine(profile.folderPath, "SKILL.md");
            Directory.CreateDirectory(profile.folderPath);

            StringBuilder builder = new StringBuilder();
            string skillName = profile.skillName.NullOrEmpty() ? FolderNameFor(profile) : profile.skillName;
            skillName = NormalizeSkillName(skillName);
            profile.skillName = skillName;
            profile.displayName = profile.label;
            builder.AppendLine("---");
            builder.AppendLine("name: " + skillName);
            if (!profile.label.NullOrEmpty() && profile.label != skillName)
            {
                builder.AppendLine("displayName: " + QuoteYamlScalar(profile.label));
            }
            builder.AppendLine("description: " + QuoteYamlScalar(profile.description ?? ""));
            builder.AppendLine("enabled: " + (profile.enabled ? "true" : "false"));
            if (!profile.activation.NullOrEmpty() && profile.activation != "auto")
            {
                builder.AppendLine("activation: " + profile.activation);
            }
            AppendMarkdownList(builder, "triggerHints", profile.triggerHints);
            AppendMarkdownList(builder, "allowedTools", profile.allowedTools);
            builder.AppendLine("---");
            builder.AppendLine();
            builder.AppendLine(profile.prompt ?? "");
            File.WriteAllText(profile.filePath, builder.ToString());
        }

        private static void AppendMarkdownList(StringBuilder builder, string name, List<string> values)
        {
            values = CleanList(values);
            if (values.Count == 0)
            {
                return;
            }

            builder.AppendLine(name + ":");
            for (int i = 0; i < values.Count; i++)
            {
                builder.AppendLine("- " + QuoteYamlScalar(values[i]));
            }
        }

        private sealed class ReferenceSnippet
        {
            public string relativePath = "";
            public string text = "";
            public int score;
        }

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
                Log.Warning("[Deepseek The Orca] Failed to scan skill references " + referenceRoot + ": " + ex.Message);
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
                Log.Warning("[Deepseek The Orca] Failed to read skill reference " + file + ": " + ex.Message);
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
            public List<string> allowedTools = new List<string>();
            public string instructions = "";
        }

        private static SkillMarkdown ParseSkillMarkdown(string text, string folderName, string filePath)
        {
            SkillMarkdown result = new SkillMarkdown();
            text = text ?? "";
            string metadata = "";
            string instructions = text;
            if (text.StartsWith("---"))
            {
                int start = text.IndexOf('\n');
                int end = start < 0 ? -1 : text.IndexOf("\n---", start + 1, StringComparison.Ordinal);
                if (end >= 0)
                {
                    metadata = text.Substring(start + 1, end - start - 1);
                    int instructionStart = end + 4;
                    instructions = instructionStart >= text.Length ? "" : text.Substring(instructionStart).TrimStart('\r', '\n');
                }
            }

            ParseMetadata(metadata, result, folderName, filePath);
            result.instructions = instructions.Trim();
            return result;
        }

        private static void ParseMetadata(string metadata, SkillMarkdown result, string folderName, string filePath)
        {
            if (metadata.NullOrEmpty() || result == null)
            {
                return;
            }

            string currentList = "";
            string[] lines = metadata.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                string rawLine = lines[i];
                string line = rawLine.Trim();
                if (line.NullOrEmpty() || line.StartsWith("#"))
                {
                    continue;
                }

                if (line.StartsWith("- "))
                {
                    if (currentList.NullOrEmpty())
                    {
                        WarnSkillMetadata(filePath, i + 1, "list item without a list key");
                        continue;
                    }
                    string value;
                    if (!TryParseYamlScalar(line.Substring(2).Trim(), out value))
                    {
                        WarnSkillMetadata(filePath, i + 1, "invalid list item");
                        continue;
                    }
                    AddMetadataListValue(result, currentList, value);
                    continue;
                }

                int separator = FindYamlKeySeparator(line);
                if (separator < 0)
                {
                    WarnSkillMetadata(filePath, i + 1, "expected 'key: value' or '- item'");
                    continue;
                }

                string key = line.Substring(0, separator).Trim();
                string valueText = line.Substring(separator + 1).Trim();
                currentList = "";
                switch (key)
                {
                    case "name":
                        if (TryParseYamlScalar(valueText, out result.name))
                        {
                            result.name = result.name.Trim();
                        }
                        else
                        {
                            WarnSkillMetadata(filePath, i + 1, "invalid name scalar");
                        }
                        break;
                    case "displayName":
                        if (TryParseYamlScalar(valueText, out result.displayName))
                        {
                            result.displayName = result.displayName.Trim();
                        }
                        else
                        {
                            WarnSkillMetadata(filePath, i + 1, "invalid displayName scalar");
                        }
                        break;
                    case "description":
                        if (TryParseYamlScalar(valueText, out result.description))
                        {
                            result.description = result.description.Trim();
                        }
                        else
                        {
                            WarnSkillMetadata(filePath, i + 1, "invalid description scalar");
                        }
                        break;
                    case "enabled":
                    case "defaultEnabled":
                        bool enabled;
                        string enabledValue;
                        if (TryParseYamlScalar(valueText, out enabledValue) && bool.TryParse(enabledValue, out enabled))
                        {
                            result.enabled = enabled;
                        }
                        else
                        {
                            WarnSkillMetadata(filePath, i + 1, "enabled must be true or false");
                        }
                        break;
                    case "activation":
                        string activation;
                        if (TryParseYamlScalar(valueText, out activation))
                        {
                            result.activation = NormalizeActivation(activation);
                        }
                        else
                        {
                            WarnSkillMetadata(filePath, i + 1, "invalid activation scalar");
                        }
                        break;
                    case "triggerHints":
                    case "allowedTools":
                        currentList = key;
                        if (!valueText.NullOrEmpty())
                        {
                            List<string> values;
                            if (TryParseYamlInlineList(valueText, out values))
                            {
                                for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
                                {
                                    AddMetadataListValue(result, currentList, values[valueIndex]);
                                }
                                currentList = "";
                            }
                            else
                            {
                                string scalar;
                                if (TryParseYamlScalar(valueText, out scalar))
                                {
                                    AddMetadataListValue(result, currentList, scalar);
                                    currentList = "";
                                }
                                else
                                {
                                    WarnSkillMetadata(filePath, i + 1, "invalid list value");
                                }
                            }
                        }
                        break;
                    default:
                        WarnSkillMetadata(filePath, i + 1, "unknown metadata key '" + key + "'");
                        break;
                }
            }
        }

        private static int FindYamlKeySeparator(string line)
        {
            bool inSingle = false;
            bool inDouble = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    continue;
                }
                if (ch == '"' && !inSingle && (i == 0 || line[i - 1] != '\\'))
                {
                    inDouble = !inDouble;
                    continue;
                }
                if (ch == ':' && !inSingle && !inDouble)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryParseYamlScalar(string text, out string value)
        {
            value = "";
            text = text == null ? "" : StripYamlComment(text.Trim());
            if (text.NullOrEmpty())
            {
                return true;
            }

            if (text.StartsWith("\"", StringComparison.Ordinal))
            {
                if (!text.EndsWith("\"", StringComparison.Ordinal) || text.Length < 2)
                {
                    return false;
                }
                value = UnescapeDoubleQuotedScalar(text.Substring(1, text.Length - 2));
                return true;
            }

            if (text.StartsWith("'", StringComparison.Ordinal))
            {
                if (!text.EndsWith("'", StringComparison.Ordinal) || text.Length < 2)
                {
                    return false;
                }
                value = text.Substring(1, text.Length - 2).Replace("''", "'");
                return true;
            }

            if (text.StartsWith("[", StringComparison.Ordinal) || text.StartsWith("{", StringComparison.Ordinal))
            {
                return false;
            }

            value = text.Trim();
            return true;
        }

        private static bool TryParseYamlInlineList(string text, out List<string> values)
        {
            values = new List<string>();
            text = text == null ? "" : StripYamlComment(text.Trim());
            if (!text.StartsWith("[", StringComparison.Ordinal) || !text.EndsWith("]", StringComparison.Ordinal))
            {
                return false;
            }

            string body = text.Substring(1, text.Length - 2);
            StringBuilder item = new StringBuilder();
            bool inSingle = false;
            bool inDouble = false;
            for (int i = 0; i < body.Length; i++)
            {
                char ch = body[i];
                if (ch == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    item.Append(ch);
                    continue;
                }
                if (ch == '"' && !inSingle && (i == 0 || body[i - 1] != '\\'))
                {
                    inDouble = !inDouble;
                    item.Append(ch);
                    continue;
                }
                if (ch == ',' && !inSingle && !inDouble)
                {
                    string parsed;
                    if (!TryParseYamlScalar(item.ToString(), out parsed))
                    {
                        return false;
                    }
                    if (!parsed.NullOrEmpty())
                    {
                        values.Add(parsed);
                    }
                    item.Length = 0;
                    continue;
                }
                item.Append(ch);
            }

            if (inSingle || inDouble)
            {
                return false;
            }

            string finalValue;
            if (!TryParseYamlScalar(item.ToString(), out finalValue))
            {
                return false;
            }
            if (!finalValue.NullOrEmpty())
            {
                values.Add(finalValue);
            }

            return true;
        }

        private static string StripYamlComment(string text)
        {
            if (text.NullOrEmpty())
            {
                return "";
            }

            bool inSingle = false;
            bool inDouble = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    continue;
                }
                if (ch == '"' && !inSingle && (i == 0 || text[i - 1] != '\\'))
                {
                    inDouble = !inDouble;
                    continue;
                }
                if (ch == '#' && !inSingle && !inDouble && (i == 0 || char.IsWhiteSpace(text[i - 1])))
                {
                    return text.Substring(0, i).TrimEnd();
                }
            }

            return text;
        }

        private static string UnescapeDoubleQuotedScalar(string text)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch != '\\' || i >= text.Length - 1)
                {
                    builder.Append(ch);
                    continue;
                }

                char next = text[++i];
                switch (next)
                {
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    default:
                        builder.Append(next);
                        break;
                }
            }

            return builder.ToString();
        }

        private static string QuoteYamlScalar(string value)
        {
            value = value ?? "";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }

        private static void WarnSkillMetadata(string filePath, int line, string message)
        {
            Log.Warning("[Deepseek The Orca] Skill metadata warning in " + filePath + ":" + line + " - " + message + ".");
        }

        private static void AddMetadataListValue(SkillMarkdown result, string listName, string value)
        {
            value = value == null ? "" : value.Trim().Trim('"');
            if (value.NullOrEmpty())
            {
                return;
            }

            if (listName == "triggerHints")
            {
                result.triggerHints.Add(value);
            }
            else if (listName == "allowedTools")
            {
                result.allowedTools.Add(value);
            }
        }

        private static bool SkillMatchesTurn(OrcaSkillProfile skill, string turnText)
        {
            if (skill == null || turnText.NullOrEmpty())
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

        private static string SourceNameFor(ModContentPack mod)
        {
            if (mod == null)
            {
                return "";
            }

            string packageId = mod.PackageIdPlayerFacing.NullOrEmpty() ? mod.PackageId : mod.PackageIdPlayerFacing;
            return string.Equals(packageId, "RedstonePanda.Orca", StringComparison.OrdinalIgnoreCase) ? "Core" : mod.Name;
        }
    }
}
