using System;
using System.Collections.Generic;

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
        public List<string> contexts = new List<string>();
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
}
