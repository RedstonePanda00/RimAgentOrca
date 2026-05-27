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
                contexts = new List<string>(),
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
    }
}
