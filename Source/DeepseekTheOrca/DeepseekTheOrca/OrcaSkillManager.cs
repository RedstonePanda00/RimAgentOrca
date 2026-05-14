using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
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

        public bool IsLocal
        {
            get { return id != null && id.StartsWith(OrcaSkillManager.LocalPrefix, StringComparison.Ordinal); }
        }
    }

    public static class OrcaSkillManager
    {
        public const string LocalPrefix = "local:";
        private static readonly List<OrcaSkillProfile> localSkills = new List<OrcaSkillProfile>();
        private static bool loadedLocal;

        public static string SkillFolderPath
        {
            get { return Path.Combine(GenFilePaths.ConfigFolderPath, "DeepseekTheOrca", "Skills"); }
        }

        public static List<OrcaSkillProfile> AllSkills()
        {
            EnsureLoaded();
            return localSkills.OrderBy(profile => profile.label).ToList();
        }

        public static List<OrcaSkillProfile> EnabledSkills()
        {
            EnsureLoaded();
            return localSkills.Where(profile => profile.enabled).OrderBy(profile => profile.label).ToList();
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
                readOnly = false
            };
            profile.filePath = PathFor(profile);
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

            profile.filePath = profile.filePath.NullOrEmpty() ? PathFor(profile) : profile.filePath;
            Normalize(profile);

            XmlDocument document = new XmlDocument();
            XmlElement root = document.CreateElement("OrcaSkill");
            document.AppendChild(root);
            AppendText(document, root, "id", profile.id);
            AppendText(document, root, "label", profile.label ?? "");
            AppendText(document, root, "description", profile.description ?? "");
            AppendText(document, root, "enabled", profile.enabled ? "true" : "false");
            AppendList(document, root, "triggerHints", profile.triggerHints);
            XmlElement prompt = document.CreateElement("prompt");
            prompt.AppendChild(document.CreateCDataSection(profile.prompt ?? ""));
            root.AppendChild(prompt);
            AppendList(document, root, "allowedTools", profile.allowedTools);
            document.Save(profile.filePath);
        }

        public static void Delete(OrcaSkillProfile profile)
        {
            if (profile == null || profile.readOnly)
            {
                return;
            }

            EnsureLoaded();
            localSkills.RemoveAll(item => item.id == profile.id);
            if (!profile.filePath.NullOrEmpty() && File.Exists(profile.filePath))
            {
                File.Delete(profile.filePath);
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
            List<OrcaSkillProfile> skills = EnabledSkills();
            if (skills.Count == 0)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Enabled skill modules:");
            builder.AppendLine("Skills are optional, domain-specific behavior modules. Use a skill only when it is relevant to the latest player message, proactive trigger, or tool results. A skill prompt changes how you reason in that domain; it is not a persona.");
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
                if (!skill.prompt.NullOrEmpty())
                {
                    builder.AppendLine("Instructions:");
                    builder.AppendLine(skill.prompt.Trim());
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
            foreach (string file in Directory.GetFiles(SkillFolderPath, "*.xml"))
            {
                OrcaSkillProfile profile = LoadFile(file);
                if (profile != null)
                {
                    localSkills.Add(profile);
                }
            }
        }

        private static OrcaSkillProfile LoadFile(string file)
        {
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(file);
                XmlElement root = document.DocumentElement;
                if (root == null || root.Name != "OrcaSkill")
                {
                    return null;
                }

                string id = ReadText(root, "id");
                if (id.NullOrEmpty())
                {
                    id = LocalPrefix + Path.GetFileNameWithoutExtension(file);
                }
                if (!id.StartsWith(LocalPrefix, StringComparison.Ordinal))
                {
                    id = LocalPrefix + id;
                }

                OrcaSkillProfile profile = new OrcaSkillProfile
                {
                    id = id,
                    label = ReadText(root, "label").NullOrEmpty() ? Path.GetFileNameWithoutExtension(file) : ReadText(root, "label"),
                    description = ReadText(root, "description"),
                    enabled = ReadBool(root, "enabled", true),
                    triggerHints = ReadList(root, "triggerHints"),
                    prompt = ReadText(root, "prompt"),
                    allowedTools = ReadList(root, "allowedTools"),
                    readOnly = false,
                    filePath = file
                };
                Normalize(profile);
                return profile;
            }
            catch (Exception ex)
            {
                Log.Warning("[Deepseek The Orca] Failed to load skill file " + file + ": " + ex.Message);
                return null;
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

        private static string PathFor(OrcaSkillProfile profile)
        {
            string name = profile.id == null ? Guid.NewGuid().ToString("N") : profile.id.Replace(LocalPrefix, "");
            name = Regex.Replace(name, "[^A-Za-z0-9_.-]", "_");
            return Path.Combine(SkillFolderPath, name + ".xml");
        }

        private static void AppendText(XmlDocument document, XmlElement root, string name, string value)
        {
            XmlElement element = document.CreateElement(name);
            element.InnerText = value ?? "";
            root.AppendChild(element);
        }

        private static void AppendList(XmlDocument document, XmlElement root, string name, List<string> values)
        {
            XmlElement list = document.CreateElement(name);
            foreach (string value in CleanList(values))
            {
                XmlElement item = document.CreateElement("li");
                item.InnerText = value;
                list.AppendChild(item);
            }
            root.AppendChild(list);
        }

        private static string ReadText(XmlElement root, string name)
        {
            XmlNode node = root.SelectSingleNode(name);
            return node == null ? "" : node.InnerText;
        }

        private static bool ReadBool(XmlElement root, string name, bool defaultValue)
        {
            string text = ReadText(root, name);
            bool parsed;
            return bool.TryParse(text, out parsed) ? parsed : defaultValue;
        }

        private static List<string> ReadList(XmlElement root, string name)
        {
            XmlNode parent = root.SelectSingleNode(name);
            List<string> values = new List<string>();
            if (parent == null)
            {
                return values;
            }

            foreach (XmlNode node in parent.SelectNodes("li"))
            {
                if (node != null && !node.InnerText.NullOrEmpty())
                {
                    values.Add(node.InnerText);
                }
            }
            return CleanList(values);
        }

        private static string SafeLine(string value)
        {
            return (value ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
