using System.Collections.Generic;

namespace DeepseekTheOrca
{
    public sealed class OrcaKnowledgeEntry
    {
        public string id = "";
        public string label = "";
        public List<string> aliases = new List<string>();
        public List<string> categories = new List<string>();
        public string text = "";
        public int priority;
        public string scope = "global";
        public bool defaultEnabled = true;
        public bool readOnly;
        public string source = "";
    }
}
