using System.Collections.Generic;
using System.Linq;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaSettingsTabRegistry
    {
        public static List<OrcaSettingsTab> CreateTabs()
        {
            return new List<OrcaSettingsTab>
            {
                new OrcaProviderSettingsTab(),
                new OrcaAgentModelSettingsTab(),
                new OrcaPersonaSettingsTab(),
                new OrcaSkillSettingsTab(),
                new OrcaPluginSettingsTab(),
                new OrcaToolsSettingsTab(),
                new OrcaMcpSettingsTab(),
                new OrcaDebugSettingsTab()
            };
        }

        public static List<OrcaSettingsTab> VisibleTabs(List<OrcaSettingsTab> tabs, OrcaSettingsContext context)
        {
            if (tabs == null)
            {
                return new List<OrcaSettingsTab>();
            }

            return tabs.Where(tab => tab.Visible(context))
                .OrderBy(tab => tab.Order)
                .ThenBy(tab => tab.Label)
                .ToList();
        }

        public static OrcaSettingsTab FirstVisibleTab(List<OrcaSettingsTab> tabs, OrcaSettingsContext context)
        {
            List<OrcaSettingsTab> visible = VisibleTabs(tabs, context);
            return visible.Count == 0 ? null : visible[0];
        }

        public static OrcaSettingsTab FindVisibleTab(List<OrcaSettingsTab> tabs, OrcaSettingsContext context, string id)
        {
            if (id.NullOrEmpty())
            {
                return null;
            }

            List<OrcaSettingsTab> visible = VisibleTabs(tabs, context);
            for (int i = 0; i < visible.Count; i++)
            {
                if (visible[i].Id == id)
                {
                    return visible[i];
                }
            }

            return null;
        }
    }
}
