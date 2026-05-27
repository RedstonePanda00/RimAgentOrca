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
            profile.contexts = CleanContextTags(profile.contexts);
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

        private static List<string> CleanContextTags(IEnumerable<string> values)
        {
            if (values == null)
            {
                return new List<string>();
            }

            return values.Select(NormalizeContextTag)
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
                    contexts = markdown.contexts,
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
            AppendMarkdownList(builder, "contexts", profile.contexts);
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
