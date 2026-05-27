using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaSkillManagerWindow : Window
    {
        private Vector2 scrollPosition;

        public OrcaSkillManagerWindow()
        {
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(780f, 560f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "DTO_SkillManage".Translate());
            Text.Font = GameFont.Small;

            float y = inRect.y + 42f;
            if (Widgets.ButtonText(new Rect(inRect.x, y, 180f, 32f), "DTO_SkillNew".Translate()))
            {
                OrcaSkillProfile profile = OrcaSkillManager.CreateLocal();
                Find.WindowStack.Add(new OrcaSkillEditorWindow(profile));
            }

            if (Widgets.ButtonText(new Rect(inRect.x + 190f, y, 180f, 32f), "DTO_SkillReload".Translate()))
            {
                OrcaSkillManager.ReloadLocal();
            }

            y += 44f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "DTO_SkillFolder".Translate() + ": " + OrcaSkillManager.SkillFolderPath);
            y += 32f;

            List<OrcaSkillProfile> skills = OrcaSkillManager.AllSkills();
            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.height - y);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, skills.Count * 96f + 8f));
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            float rowY = 0f;
            for (int i = 0; i < skills.Count; i++)
            {
                DrawSkillRow(skills[i], new Rect(0f, rowY, viewRect.width, 90f));
                rowY += 96f;
            }

            if (skills.Count == 0)
            {
                Widgets.Label(new Rect(0f, 0f, viewRect.width, 30f), "DTO_SkillNoSkills".Translate());
            }

            Widgets.EndScrollView();
        }

        private static void DrawSkillRow(OrcaSkillProfile profile, Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.09f, 0.7f));
            Rect textRect = new Rect(rect.x + 8f, rect.y + 6f, rect.width - 260f, 24f);
            string state = profile.enabled ? "DTO_SkillEnabled".Translate().ToString() : "DTO_SkillDisabled".Translate().ToString();
            string title = profile.label + " (" + state + ")" + (profile.readOnly ? " (" + "DTO_SkillReadOnly".Translate().ToString() + ")" : "");
            Widgets.Label(textRect, title);
            Widgets.Label(new Rect(textRect.x, textRect.yMax + 4f, textRect.width, 24f), profile.description ?? "");

            Rect enableRect = new Rect(rect.xMax - 242f, rect.y + 8f, 76f, 28f);
            bool enabled = profile.enabled;
            if (Widgets.ButtonText(enableRect, enabled ? "DTO_SkillDisable".Translate() : "DTO_SkillEnable".Translate()))
            {
                OrcaSkillManager.SetEnabled(profile, !profile.enabled);
            }

            Rect editRect = new Rect(enableRect.xMax + 8f, enableRect.y, 72f, 28f);
            if (profile.readOnly)
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(editRect, "DTO_SkillEdit".Translate());
                GUI.color = Color.white;
            }
            else if (Widgets.ButtonText(editRect, "DTO_SkillEdit".Translate()))
            {
                Find.WindowStack.Add(new OrcaSkillEditorWindow(profile));
            }

            Rect deleteRect = new Rect(editRect.xMax + 8f, editRect.y, 72f, 28f);
            if (profile.readOnly)
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(deleteRect, "DTO_SkillDelete".Translate());
                GUI.color = Color.white;
            }
            else if (Widgets.ButtonText(deleteRect, "DTO_SkillDelete".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("DTO_SkillDeleteConfirm".Translate(profile.label), delegate
                {
                    OrcaSkillManager.Delete(profile);
                }, destructive: true));
            }

            if (profile.contexts != null && profile.contexts.Count > 0)
            {
                Widgets.Label(new Rect(textRect.x, textRect.yMax + 30f, textRect.width, 22f), SourceText(profile) + " | " + "DTO_SkillContexts".Translate() + ": " + string.Join(", ", profile.contexts.Take(5).ToArray()));
            }
            else if (profile.triggerHints != null && profile.triggerHints.Count > 0)
            {
                Widgets.Label(new Rect(textRect.x, textRect.yMax + 30f, textRect.width, 22f), SourceText(profile) + " | " + "DTO_SkillTriggerHints".Translate() + ": " + string.Join(", ", profile.triggerHints.Take(5).ToArray()));
            }
            else
            {
                Widgets.Label(new Rect(textRect.x, textRect.yMax + 30f, textRect.width, 22f), SourceText(profile));
            }
        }

        private static string SourceText(OrcaSkillProfile profile)
        {
            string source = profile == null ? "" : profile.sourceMod;
            if (source.NullOrEmpty())
            {
                source = "DTO_ExtensionSourceLocal".Translate().ToString();
            }

            return "DTO_ExtensionSource".Translate() + ": " + source;
        }
    }

    public sealed class OrcaSkillEditorWindow : Window
    {
        private readonly OrcaSkillProfile profile;
        private string labelBuffer;
        private string descriptionBuffer;
        private string triggerHintsBuffer;
        private string contextsBuffer;
        private string allowedToolsBuffer;
        private string promptBuffer;
        private bool enabledBuffer;
        private Vector2 promptScrollPosition;

        public OrcaSkillEditorWindow(OrcaSkillProfile profile)
        {
            this.profile = profile;
            labelBuffer = profile == null ? "" : profile.label;
            descriptionBuffer = profile == null ? "" : profile.description;
            triggerHintsBuffer = profile == null || profile.triggerHints == null ? "" : string.Join("\n", profile.triggerHints.ToArray());
            contextsBuffer = profile == null || profile.contexts == null ? "" : string.Join("\n", profile.contexts.ToArray());
            allowedToolsBuffer = profile == null || profile.allowedTools == null ? "" : string.Join("\n", profile.allowedTools.ToArray());
            promptBuffer = profile == null ? "" : profile.prompt;
            enabledBuffer = profile == null || profile.enabled;
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(780f, 680f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (profile == null || profile.readOnly)
            {
                Widgets.Label(inRect, "DTO_SkillReadOnly".Translate());
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "DTO_SkillEdit".Translate());
            Text.Font = GameFont.Small;

            float y = inRect.y + 42f;
            Rect enabledRect = new Rect(inRect.x, y, inRect.width, 28f);
            Widgets.CheckboxLabeled(enabledRect, "DTO_SkillEnableThis".Translate(), ref enabledBuffer, false, null, null, false);
            y += 36f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "DTO_SkillName".Translate());
            y += 26f;
            labelBuffer = Widgets.TextField(new Rect(inRect.x, y, inRect.width, 28f), labelBuffer ?? "");
            y += 36f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "DTO_SkillDescription".Translate());
            y += 26f;
            descriptionBuffer = Widgets.TextField(new Rect(inRect.x, y, inRect.width, 28f), descriptionBuffer ?? "");
            y += 36f;

            float columnWidth = (inRect.width - 20f) / 3f;
            Widgets.Label(new Rect(inRect.x, y, columnWidth, 24f), "DTO_SkillTriggerHints".Translate());
            Widgets.Label(new Rect(inRect.x + columnWidth + 10f, y, columnWidth, 24f), "DTO_SkillContexts".Translate());
            Widgets.Label(new Rect(inRect.x + (columnWidth + 10f) * 2f, y, columnWidth, 24f), "DTO_SkillAllowedTools".Translate());
            y += 26f;
            triggerHintsBuffer = Widgets.TextArea(new Rect(inRect.x, y, columnWidth, 82f), triggerHintsBuffer ?? "");
            contextsBuffer = Widgets.TextArea(new Rect(inRect.x + columnWidth + 10f, y, columnWidth, 82f), contextsBuffer ?? "");
            allowedToolsBuffer = Widgets.TextArea(new Rect(inRect.x + (columnWidth + 10f) * 2f, y, columnWidth, 82f), allowedToolsBuffer ?? "");
            y += 92f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "DTO_SkillPrompt".Translate());
            y += 26f;
            Rect promptOuter = new Rect(inRect.x, y, inRect.width, inRect.height - y - 48f);
            float viewHeight = Mathf.Max(promptOuter.height, Text.CalcHeight(promptBuffer ?? "", promptOuter.width - 20f) + 80f);
            Rect promptView = new Rect(0f, 0f, promptOuter.width - 16f, viewHeight);
            Widgets.BeginScrollView(promptOuter, ref promptScrollPosition, promptView);
            promptBuffer = Widgets.TextArea(new Rect(0f, 0f, promptView.width, viewHeight), promptBuffer ?? "");
            Widgets.EndScrollView();

            Rect saveRect = new Rect(inRect.xMax - 170f, inRect.yMax - 36f, 80f, 32f);
            if (Widgets.ButtonText(saveRect, "DTO_SkillSave".Translate()))
            {
                profile.label = labelBuffer.NullOrEmpty() ? "New Skill" : labelBuffer;
                profile.description = descriptionBuffer ?? "";
                profile.enabled = enabledBuffer;
                profile.triggerHints = SplitLines(triggerHintsBuffer);
                profile.contexts = SplitLines(contextsBuffer);
                profile.allowedTools = SplitLines(allowedToolsBuffer);
                profile.prompt = promptBuffer ?? "";
                OrcaSkillManager.Save(profile);
                OrcaChatWindowManager.Session.Clear();
                Close();
            }

            Rect cancelRect = new Rect(saveRect.xMax + 10f, saveRect.y, 80f, 32f);
            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
            {
                Close();
            }
        }

        private static List<string> SplitLines(string text)
        {
            if (text.NullOrEmpty())
            {
                return new List<string>();
            }

            return text.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !value.NullOrEmpty())
                .Distinct()
                .ToList();
        }
    }
}
