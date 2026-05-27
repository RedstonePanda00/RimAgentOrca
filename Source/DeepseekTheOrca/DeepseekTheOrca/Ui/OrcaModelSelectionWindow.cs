using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaModelSelectionWindow : Window
    {
        private readonly OrcaLlmModelRole role;
        private readonly bool allowFallback;
        private readonly List<OrcaModelOption> options;
        private Vector2 scrollPosition;
        private string filter = "";

        public OrcaModelSelectionWindow(OrcaLlmModelRole role, bool allowFallback, List<OrcaModelOption> options)
        {
            this.role = role;
            this.allowFallback = allowFallback;
            this.options = options ?? new List<OrcaModelOption>();
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(760f, 520f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "DTO_ModelSelectTitle".Translate() + ": " + RoleLabel(role));
            Text.Font = GameFont.Small;

            Rect filterLabelRect = new Rect(inRect.x, inRect.y + 42f, 80f, 28f);
            Widgets.Label(filterLabelRect, "DTO_ModelFilter".Translate());
            Rect filterRect = new Rect(filterLabelRect.xMax + 8f, filterLabelRect.y, inRect.width - filterLabelRect.width - 8f, 28f);
            filter = Widgets.TextField(filterRect, filter ?? "");

            float y = filterRect.yMax + 10f;
            if (allowFallback)
            {
                Rect fallbackRect = new Rect(inRect.x, y, inRect.width, 32f);
                if (Widgets.ButtonText(fallbackRect, "DTO_ModelUseFallback".Translate()))
                {
                    DeepseekTheOrcaMod.SetModelReferenceForRole(role, "");
                    LlmConnectionTester.Reset();
                    Close();
                    return;
                }

                y += 42f;
            }

            List<OrcaModelOption> filtered = FilteredOptions();
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "DTO_ModelFilterCount".Translate(filtered.Count, options.Count));
            y += 28f;

            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.height - y);
            float rowHeight = 34f;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, filtered.Count * rowHeight + 8f));
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float rowY = 0f;
            if (filtered.Count == 0)
            {
                Widgets.Label(new Rect(0f, rowY, viewRect.width, 28f), "DTO_ModelNoMatchingModels".Translate());
            }

            for (int i = 0; i < filtered.Count; i++)
            {
                OrcaModelOption option = filtered[i];
                Rect rowRect = new Rect(0f, rowY, viewRect.width, rowHeight - 4f);
                if (Widgets.ButtonText(rowRect, option.label))
                {
                    DeepseekTheOrcaMod.SetModelReferenceForRole(role, option.reference);
                    LlmConnectionTester.Reset();
                    Close();
                    break;
                }

                rowY += rowHeight;
            }

            Widgets.EndScrollView();
        }

        private List<OrcaModelOption> FilteredOptions()
        {
            string needle = (filter ?? "").Trim().ToLowerInvariant();
            if (needle.NullOrEmpty())
            {
                return options;
            }

            return options.Where(option =>
                option != null
                && ((option.label != null && option.label.ToLowerInvariant().Contains(needle))
                    || (option.modelId != null && option.modelId.ToLowerInvariant().Contains(needle))
                    || (option.connection != null && option.connection.name != null && option.connection.name.ToLowerInvariant().Contains(needle)))).ToList();
        }

        private static string RoleLabel(OrcaLlmModelRole role)
        {
            switch (role)
            {
                case OrcaLlmModelRole.Controller:
                    return "DTO_ModelController".Translate();
                case OrcaLlmModelRole.Decision:
                    return "DTO_ModelDecision".Translate();
                case OrcaLlmModelRole.Dialogue:
                    return "DTO_ModelDialogue".Translate();
                case OrcaLlmModelRole.Tool:
                    return "DTO_ModelTool".Translate();
                case OrcaLlmModelRole.Vision:
                    return "DTO_ModelVision".Translate();
                case OrcaLlmModelRole.WebSearch:
                    return "DTO_ModelWebSearch".Translate();
                default:
                    return "DTO_ModelFallback".Translate();
            }
        }
    }
}
