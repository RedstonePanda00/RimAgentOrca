using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaSettingsLayout
    {
        public static void Draw(Rect inRect, OrcaSettingsContext context, List<OrcaSettingsTab> tabInstances, ref string selectedTabId)
        {
            List<OrcaSettingsTab> tabs = OrcaSettingsTabRegistry.VisibleTabs(tabInstances, context);
            if (tabs.Count == 0)
            {
                Widgets.Label(inRect, "No settings tabs are available.");
                return;
            }

            float gap = 12f;
            float leftWidth = Mathf.Clamp(inRect.width * 0.22f, 180f, 240f);
            Rect left = new Rect(inRect.x, inRect.y, leftWidth, inRect.height);
            Rect right = new Rect(left.xMax + gap, inRect.y, inRect.width - leftWidth - gap, inRect.height);

            OrcaSettingsWidgets.DrawPanel(left);
            OrcaSettingsWidgets.DrawPanel(right);
            DrawNavigation(left.ContractedBy(10f), tabs, context, ref selectedTabId);

            OrcaSettingsTab selected = OrcaSettingsTabRegistry.FindVisibleTab(tabInstances, context, selectedTabId) ?? tabs[0];
            selectedTabId = selected.Id;
            selected.Draw(right.ContractedBy(10f), context);
        }

        private static void DrawNavigation(Rect rect, List<OrcaSettingsTab> tabs, OrcaSettingsContext context, ref string selectedTabId)
        {
            Text.Font = GameFont.Small;
            float y = rect.y;
            for (int i = 0; i < tabs.Count; i++)
            {
                OrcaSettingsTab tab = tabs[i];
                DrawTabButton(new Rect(rect.x, y, rect.width, 34f), tab, context, ref selectedTabId);
                y += 42f;
            }
        }

        private static void DrawTabButton(Rect rect, OrcaSettingsTab tab, OrcaSettingsContext context, ref string selectedTabId)
        {
            bool selected = selectedTabId == tab.Id;
            bool hover = Mouse.IsOver(rect);
            if (selected)
            {
                Widgets.DrawBoxSolid(rect, OrcaSettingsWidgets.RowSelectedFill);
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), new Color(0.45f, 0.68f, 0.88f, 1f));
            }
            else if (hover)
            {
                Widgets.DrawBoxSolid(rect, OrcaSettingsWidgets.RowHoverFill);
            }

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 10f, rect.y, rect.width - 12f, rect.height), tab.Label);
            Text.Anchor = TextAnchor.UpperLeft;

            if (Widgets.ButtonInvisible(rect))
            {
                selectedTabId = tab.Id;
                tab.OnSelected(context);
            }
        }
    }
}
