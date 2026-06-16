using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaSettingsWidgets
    {
        public static readonly Color PanelFill = new Color(0.05f, 0.055f, 0.065f, 0.72f);
        public static readonly Color RowSelectedFill = new Color(0.18f, 0.23f, 0.28f, 0.86f);
        public static readonly Color RowHoverFill = new Color(0.12f, 0.14f, 0.16f, 0.72f);
        public static readonly Color BorderColor = new Color(0.42f, 0.45f, 0.48f, 1f);

        public static void DrawPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelFill);
            DrawOutline(rect);
        }

        public static void DrawOutline(Rect rect)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 1f), BorderColor);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), BorderColor);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 1f, rect.height), BorderColor);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), BorderColor);
        }

        public static string ClampText(string text, int maxChars)
        {
            if (text == null)
            {
                return "";
            }

            return text.Length <= maxChars ? text : text.Substring(0, maxChars) + "...";
        }
    }
}
