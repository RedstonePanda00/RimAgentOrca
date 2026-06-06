using System.Collections.Generic;
using Verse;

namespace DeepseekTheOrca
{
    public class OrcaKnowledgeEntryDef : Def
    {
        public List<string> aliases = new List<string>();
        public List<string> categories = new List<string>();
        public string text = "";
        public int priority;
        public string scope = "global";
        public bool defaultEnabled = true;
    }
}
