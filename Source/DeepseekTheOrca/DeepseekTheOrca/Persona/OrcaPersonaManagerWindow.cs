using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaPersonaManagerWindow : Window
    {
        private Vector2 scrollPosition;

        public OrcaPersonaManagerWindow()
        {
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(760f, 520f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "DTO_ChatPersonaManage".Translate());
            Text.Font = GameFont.Small;

            float y = inRect.y + 42f;
            if (Widgets.ButtonText(new Rect(inRect.x, y, 180f, 32f), "DTO_ChatPersonaNew".Translate()))
            {
                OrcaChatPersonaProfile profile = OrcaChatPersonaManager.CreateLocal();
                Find.WindowStack.Add(new OrcaPersonaEditorWindow(profile));
            }

            if (Widgets.ButtonText(new Rect(inRect.x + 190f, y, 180f, 32f), "DTO_ChatPersonaReload".Translate()))
            {
                OrcaChatPersonaManager.ReloadLocal();
                OrcaStorytellerAppearance.ApplyCurrent();
            }

            y += 44f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "DTO_ChatPersonaFolder".Translate() + ": " + OrcaChatPersonaManager.PersonaFolderPath);
            y += 32f;

            List<OrcaChatPersonaProfile> personas = OrcaChatPersonaManager.AllPersonas();
            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.height - y);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, personas.Count * 88f + 8f));
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            float rowY = 0f;
            for (int i = 0; i < personas.Count; i++)
            {
                DrawPersonaRow(personas[i], new Rect(0f, rowY, viewRect.width, 82f));
                rowY += 88f;
            }

            Widgets.EndScrollView();
        }

        private static void DrawPersonaRow(OrcaChatPersonaProfile profile, Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.09f, 0.7f));
            Rect textRect = new Rect(rect.x + 8f, rect.y + 6f, rect.width - 250f, 24f);
            string title = profile.label + " (" + SourceName(profile) + ", " + "DTO_ChatPersonaPriority".Translate() + " " + profile.priority + ")";
            Widgets.Label(textRect, title);
            Widgets.Label(new Rect(textRect.x, textRect.yMax + 4f, textRect.width, 24f), profile.description ?? "");

            Rect selectRect = new Rect(rect.xMax - 232f, rect.y + 8f, 72f, 28f);
            if (Widgets.ButtonText(selectRect, "DTO_ChatPersonaSelect".Translate()))
            {
                DeepseekTheOrcaMod.Settings.chatPersonaDefName = profile.id;
                if (DeepseekTheOrcaMod.Instance != null)
                {
                    DeepseekTheOrcaMod.Instance.WriteSettings();
                }
                OrcaStorytellerAppearance.Apply(profile);
                OrcaChatWindowManager.Session.Clear();
            }

            Rect editRect = new Rect(selectRect.xMax + 8f, selectRect.y, 72f, 28f);
            if (!profile.readOnly && Widgets.ButtonText(editRect, "DTO_ChatPersonaEdit".Translate()))
            {
                Find.WindowStack.Add(new OrcaPersonaEditorWindow(profile));
            }

            Rect deleteRect = new Rect(editRect.xMax + 8f, editRect.y, 72f, 28f);
            if (!profile.readOnly && Widgets.ButtonText(deleteRect, "DTO_ChatPersonaDelete".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("DTO_ChatPersonaDeleteConfirm".Translate(profile.label), delegate
                {
                    OrcaChatPersonaManager.Delete(profile);
                }, destructive: true));
            }
        }

        private static string SourceName(OrcaChatPersonaProfile profile)
        {
            string source = profile == null ? "" : profile.sourceMod;
            if (source.NullOrEmpty())
            {
                source = "DTO_ExtensionSourceLocal".Translate().ToString();
            }

            return source;
        }
    }

    public sealed class OrcaPersonaEditorWindow : Window
    {
        private readonly OrcaChatPersonaProfile profile;
        private string labelBuffer;
        private string descriptionBuffer;
        private string narrativeTendencyBuffer;
        private string controllerRoutingTendencyBuffer;
        private string priorityBuffer;
        private string storytellerLabelBuffer;
        private string storytellerDescriptionBuffer;
        private string portraitLargePathBuffer;
        private string portraitTinyPathBuffer;
        private string promptBuffer;
        private Vector2 formScrollPosition;

        public OrcaPersonaEditorWindow(OrcaChatPersonaProfile profile)
        {
            this.profile = profile;
            labelBuffer = profile == null ? "" : profile.label;
            descriptionBuffer = profile == null ? "" : profile.description;
            narrativeTendencyBuffer = profile == null ? "" : profile.narrativeTendency;
            controllerRoutingTendencyBuffer = profile == null ? "" : profile.controllerRoutingTendency;
            priorityBuffer = profile == null ? "0" : profile.priority.ToString();
            OrcaChatPersonaManager.NormalizeAppearance(profile);
            storytellerLabelBuffer = profile == null ? "" : profile.storytellerLabel;
            storytellerDescriptionBuffer = profile == null ? "" : profile.storytellerDescription;
            portraitLargePathBuffer = profile == null ? "" : profile.storytellerPortraitLargePath;
            portraitTinyPathBuffer = profile == null ? "" : profile.storytellerPortraitTinyPath;
            promptBuffer = profile == null ? "" : profile.prompt;
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(900f, 760f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (profile == null || profile.readOnly)
            {
                Widgets.Label(inRect, "DTO_ChatPersonaReadOnly".Translate());
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "DTO_ChatPersonaEdit".Translate());
            Text.Font = GameFont.Small;

            Rect buttonsRect = new Rect(inRect.x, inRect.yMax - 36f, inRect.width, 36f);
            Rect formRect = new Rect(inRect.x, inRect.y + 42f, inRect.width, inRect.height - 88f);
            float viewWidth = formRect.width - 16f;
            float promptHeight = Mathf.Max(260f, Text.CalcHeight(promptBuffer ?? "", viewWidth - 16f) + 56f);
            float viewHeight = 738f + promptHeight;
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
            Widgets.BeginScrollView(formRect, ref formScrollPosition, viewRect);

            float y = 0f;
            float leftWidth = Mathf.Max(380f, viewWidth * 0.58f);
            float rightWidth = viewWidth - leftWidth - 12f;
            Rect nameRect = new Rect(0f, y, leftWidth, 56f);
            Rect priorityRect = new Rect(nameRect.xMax + 12f, y, rightWidth, 56f);
            labelBuffer = DrawTextField(nameRect, "DTO_ChatPersonaName".Translate(), labelBuffer);
            priorityBuffer = DrawTextField(priorityRect, "DTO_ChatPersonaPriority".Translate(), priorityBuffer);
            y += 66f;

            descriptionBuffer = DrawTextArea(new Rect(0f, y, viewWidth, 76f), "DTO_ChatPersonaDescription".Translate(), descriptionBuffer);
            y += 88f;

            narrativeTendencyBuffer = DrawTextArea(new Rect(0f, y, viewWidth, 76f), "DTO_ChatPersonaNarrativeTendency".Translate(), narrativeTendencyBuffer);
            y += 88f;

            controllerRoutingTendencyBuffer = DrawTextArea(new Rect(0f, y, viewWidth, 76f), "DTO_ChatPersonaControllerRoutingTendency".Translate(), controllerRoutingTendencyBuffer);
            y += 88f;

            Widgets.Label(new Rect(0f, y, viewWidth, 24f), "DTO_StorytellerAppearanceTitle".Translate());
            y += 30f;
            storytellerLabelBuffer = DrawTextField(new Rect(0f, y, viewWidth, 56f), "DTO_StorytellerLabel".Translate(), storytellerLabelBuffer);
            y += 66f;
            storytellerDescriptionBuffer = DrawTextArea(new Rect(0f, y, viewWidth, 76f), "DTO_StorytellerDescription".Translate(), storytellerDescriptionBuffer);
            y += 88f;

            float halfWidth = (viewWidth - 12f) * 0.5f;
            portraitLargePathBuffer = DrawTextField(new Rect(0f, y, halfWidth, 56f), "DTO_StorytellerPortraitLargePath".Translate(), portraitLargePathBuffer);
            portraitTinyPathBuffer = DrawTextField(new Rect(halfWidth + 12f, y, halfWidth, 56f), "DTO_StorytellerPortraitTinyPath".Translate(), portraitTinyPathBuffer);
            y += 66f;

            OrcaChatPersonaProfile preview = new OrcaChatPersonaProfile
            {
                label = labelBuffer,
                description = descriptionBuffer,
                storytellerLabel = storytellerLabelBuffer,
                storytellerDescription = storytellerDescriptionBuffer,
                storytellerPortraitLargePath = portraitLargePathBuffer,
                storytellerPortraitTinyPath = portraitTinyPathBuffer
            };
            Widgets.Label(new Rect(0f, y, viewWidth, 24f), "DTO_StorytellerPortraitResolvedLargePath".Translate() + ": " + OrcaStorytellerAppearance.LargePortraitPath(preview));
            y += 24f;
            Widgets.Label(new Rect(0f, y, viewWidth, 24f), "DTO_StorytellerPortraitResolvedTinyPath".Translate() + ": " + OrcaStorytellerAppearance.TinyPortraitPath(preview));
            y += 40f;

            Widgets.Label(new Rect(0f, y, viewWidth, 24f), "DTO_ChatPersonaPrompt".Translate());
            y += 26f;
            promptBuffer = Widgets.TextArea(new Rect(0f, y, viewWidth, promptHeight - 26f), promptBuffer ?? "");

            Widgets.EndScrollView();

            Rect saveRect = new Rect(buttonsRect.xMax - 170f, buttonsRect.y, 80f, 32f);
            if (Widgets.ButtonText(saveRect, "DTO_ChatPersonaSave".Translate()))
            {
                profile.label = labelBuffer.NullOrEmpty() ? "New Persona" : labelBuffer;
                profile.description = descriptionBuffer ?? "";
                profile.narrativeTendency = narrativeTendencyBuffer ?? "";
                profile.controllerRoutingTendency = controllerRoutingTendencyBuffer ?? "";
                int priority;
                profile.priority = int.TryParse(priorityBuffer, out priority) ? priority : 0;
                profile.storytellerLabel = storytellerLabelBuffer ?? "";
                profile.storytellerDescription = storytellerDescriptionBuffer ?? "";
                profile.storytellerPortraitLargePath = portraitLargePathBuffer ?? "";
                profile.storytellerPortraitTinyPath = portraitTinyPathBuffer ?? "";
                OrcaChatPersonaManager.NormalizeAppearance(profile);
                profile.prompt = promptBuffer ?? "";
                OrcaChatPersonaManager.Save(profile);
                if (DeepseekTheOrcaMod.Settings.chatPersonaDefName == profile.id)
                {
                    OrcaStorytellerAppearance.Apply(profile);
                    OrcaChatWindowManager.Session.Clear();
                }
                Close();
            }

            Rect cancelRect = new Rect(saveRect.xMax + 10f, saveRect.y, 80f, 32f);
            if (Widgets.ButtonText(cancelRect, "Cancel".Translate()))
            {
                Close();
            }
        }

        private static string DrawTextField(Rect rect, string label, string value)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), label);
            return Widgets.TextField(new Rect(rect.x, rect.y + 26f, rect.width, 28f), value ?? "");
        }

        private static string DrawTextArea(Rect rect, string label, string value)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), label);
            return Widgets.TextArea(new Rect(rect.x, rect.y + 26f, rect.width, rect.height - 26f), value ?? "");
        }
    }
}
