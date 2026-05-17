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
        private static readonly List<OrcaSkillProfile> localSkills = new List<OrcaSkillProfile>();
        private static bool loadedLocal;

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
            OrcaSkillProfile profile = new OrcaSkillProfile
            {
                id = LocalPrefix + Guid.NewGuid().ToString("N"),
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
            return FormatSkillPrompt(EnabledSkills(), "Enabled skill modules:");
        }

        public static string FormatActiveSkillPrompt(string turnText)
        {
            return FormatSkillPrompt(ActiveSkillsFor(turnText), "Active skill context for this turn:");
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

        private static string FormatSkillPrompt(List<OrcaSkillProfile> skills, string header)
        {
            if (skills.Count == 0)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine(header);
            builder.AppendLine("Agent skills are lightweight capability folders with metadata and instructions. Use a skill when it is relevant to the latest player message, proactive trigger, tool results, or when its activation is always. A skill changes how you perform that task; it is not a persona.");
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
                profile.label = "New Skill";
            }
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
            string name = profile.id == null ? Guid.NewGuid().ToString("N") : profile.id.Replace(LocalPrefix, "");
            name = Regex.Replace(name, "[^A-Za-z0-9_.-]", "_");
            return Path.Combine(SkillFolderPath, name);
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
                SkillMarkdown markdown = ParseSkillMarkdown(File.ReadAllText(skillFile));
                string folderName = Path.GetFileName(directory);
                OrcaSkillProfile profile = new OrcaSkillProfile
                {
                    id = readOnly ? SkillPrefix + folderName : LocalPrefix + folderName,
                    label = markdown.name.NullOrEmpty() ? folderName : markdown.name,
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
            builder.AppendLine("---");
            builder.AppendLine("name: " + (profile.label ?? ""));
            builder.AppendLine("description: " + (profile.description ?? ""));
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
                builder.AppendLine("- " + values[i]);
            }
        }

        private sealed class SkillMarkdown
        {
            public string name = "";
            public string description = "";
            public bool enabled = true;
            public string activation = "auto";
            public List<string> triggerHints = new List<string>();
            public List<string> allowedTools = new List<string>();
            public string instructions = "";
        }

        private static SkillMarkdown ParseSkillMarkdown(string text)
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

            ParseMetadata(metadata, result);
            result.instructions = instructions.Trim();
            return result;
        }

        private static void ParseMetadata(string metadata, SkillMarkdown result)
        {
            if (metadata.NullOrEmpty() || result == null)
            {
                return;
            }

            string currentList = "";
            string[] lines = metadata.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.NullOrEmpty())
                {
                    continue;
                }

                if (line.StartsWith("- "))
                {
                    AddMetadataListValue(result, currentList, line.Substring(2).Trim());
                    continue;
                }

                int separator = line.IndexOf(':');
                if (separator < 0)
                {
                    continue;
                }

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim().Trim('"');
                currentList = "";
                switch (key)
                {
                    case "name":
                        result.name = value;
                        break;
                    case "description":
                        result.description = value;
                        break;
                    case "enabled":
                    case "defaultEnabled":
                        bool enabled;
                        result.enabled = !bool.TryParse(value, out enabled) || enabled;
                        break;
                    case "activation":
                        result.activation = NormalizeActivation(value);
                        break;
                    case "triggerHints":
                    case "allowedTools":
                        currentList = key;
                        if (!value.NullOrEmpty())
                        {
                            AddMetadataListValue(result, currentList, value);
                        }
                        break;
                }
            }
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
                return skill != null && skill.activation == "always";
            }

            if (skill.activation == "always")
            {
                return true;
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
            activation = activation == null ? "" : activation.Trim().ToLowerInvariant();
            return activation == "always" ? "always" : "auto";
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
