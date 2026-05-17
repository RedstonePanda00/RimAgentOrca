using System.Collections.Generic;
using Verse;

namespace DeepseekTheOrca
{
    public class OrcaChatPersonaDef : Def
    {
        public string prompt = "";
        public string storytellerLabel = "";
        public string storytellerDescription = "";
        public string storytellerPortraitFolder = "";
        public string storytellerPortraitLargeName = "";
        public string storytellerPortraitTinyName = "";
        public string storytellerPortraitLargePath = "";
        public string storytellerPortraitTinyPath = "";
    }

    public class OrcaPluginDef : Def
    {
        public bool defaultEnabled;
        public string category = "";
        public string details = "";
        public string prompt = "";
        public List<string> triggerHints = new List<string>();
        public List<string> allowedTools = new List<string>();
    }
}
