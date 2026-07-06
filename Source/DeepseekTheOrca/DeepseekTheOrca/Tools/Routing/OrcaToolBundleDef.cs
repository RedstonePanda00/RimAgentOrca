using System.Collections.Generic;
using System.Linq;
using Verse;

namespace DeepseekTheOrca
{
    public class OrcaToolBundleDef : Def
    {
        public string toolBundleId = "";
        public List<string> aliases = new List<string>();
        public List<string> toolNames = new List<string>();
        public List<string> personaIds = new List<string>();
        public float priority;
        public bool topKEligible = true;
        public bool includeByDefault;
        public bool allowDuringProactive = true;
        public bool requiresExplicitIntent;
        public List<string> exposeToRoles = new List<string>();

        public string BundleId
        {
            get { return toolBundleId.NullOrEmpty() ? defName : toolBundleId; }
        }

        public string SemanticText
        {
            get
            {
                return string.Join(" ", new[]
                {
                    label ?? "",
                    description ?? "",
                    string.Join(" ", (aliases ?? new List<string>()).ToArray()),
                    string.Join(" ", (toolNames ?? new List<string>()).ToArray())
                });
            }
        }

        public bool ExposesToRole(OrcaLlmModelRole role)
        {
            if (exposeToRoles == null || exposeToRoles.Count == 0)
            {
                return role == OrcaLlmModelRole.Tool;
            }

            string roleText = OrcaChatRoleUtility.ModelRoleLabel(role).ToLowerInvariant();
            return exposeToRoles.Any(item => string.Equals((item ?? "").Trim(), roleText, System.StringComparison.OrdinalIgnoreCase));
        }

        public bool AllowsCurrentPersona()
        {
            return OrcaChatPersonaManager.PersonaIdListAllowsCurrent(personaIds);
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (BundleId.NullOrEmpty())
            {
                yield return "toolBundleId or defName must be set.";
            }
            if (toolNames == null || toolNames.Count == 0)
            {
                yield return "toolNames must contain at least one tool name.";
            }
        }
    }
}
