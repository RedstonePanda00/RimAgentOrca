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
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, personas.Count * 78f + 8f));
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            float rowY = 0f;
            for (int i = 0; i < personas.Count; i++)
            {
                DrawPersonaRow(personas[i], new Rect(0f, rowY, viewRect.width, 72f));
                rowY += 78f;
            }

            Widgets.EndScrollView();
        }

        private static void DrawPersonaRow(OrcaChatPersonaProfile profile, Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.09f, 0.7f));
            Rect textRect = new Rect(rect.x + 8f, rect.y + 6f, rect.width - 250f, 24f);
            string title = profile.label + (profile.readOnly ? " (" + "DTO_ChatPersonaReadOnly".Translate().ToString() + ")" : "");
            Widgets.Label(textRect, title);
            Widgets.Label(new Rect(textRect.x, textRect.yMax + 4f, textRect.width, 24f), profile.description ?? "");

            Rect selectRect = new Rect(rect.xMax - 232f, rect.y + 8f, 72f, 28f);
            if (Widgets.ButtonText(selectRect, "DTO_ChatPersonaSelect".Translate()))
            {
                DeepseekTheOrcaMod.Settings.chatPersonaDefName = profile.id;
                OrcaStorytellerAppearance.Apply(profile);
                OrcaChatWindowManager.Session.Clear();
            }

            Rect editRect = new Rect(selectRect.xMax + 8f, selectRect.y, 72f, 28f);
            if (profile.readOnly)
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(editRect, "DTO_ChatPersonaEdit".Translate());
                GUI.color = Color.white;
            }
            else if (Widgets.ButtonText(editRect, "DTO_ChatPersonaEdit".Translate()))
            {
                Find.WindowStack.Add(new OrcaPersonaEditorWindow(profile));
            }

            Rect deleteRect = new Rect(editRect.xMax + 8f, editRect.y, 72f, 28f);
            if (profile.readOnly)
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(deleteRect, "DTO_ChatPersonaDelete".Translate());
                GUI.color = Color.white;
            }
            else if (Widgets.ButtonText(deleteRect, "DTO_ChatPersonaDelete".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("DTO_ChatPersonaDeleteConfirm".Translate(profile.label), delegate
                {
                    OrcaChatPersonaManager.Delete(profile);
                }, destructive: true));
            }
        }
    }

    public sealed class OrcaPersonaEditorWindow : Window
    {
        private readonly OrcaChatPersonaProfile profile;
        private string labelBuffer;
        private string descriptionBuffer;
        private string storytellerLabelBuffer;
        private string storytellerDescriptionBuffer;
        private string portraitFolderBuffer;
        private string portraitLargeNameBuffer;
        private string portraitTinyNameBuffer;
        private string promptBuffer;
        private Vector2 promptScrollPosition;

        public OrcaPersonaEditorWindow(OrcaChatPersonaProfile profile)
        {
            this.profile = profile;
            labelBuffer = profile == null ? "" : profile.label;
            descriptionBuffer = profile == null ? "" : profile.description;
            OrcaChatPersonaManager.NormalizeAppearance(profile);
            storytellerLabelBuffer = profile == null ? "" : profile.storytellerLabel;
            storytellerDescriptionBuffer = profile == null ? "" : profile.storytellerDescription;
            portraitFolderBuffer = profile == null ? "" : profile.storytellerPortraitFolder;
            portraitLargeNameBuffer = profile == null ? "" : profile.storytellerPortraitLargeName;
            portraitTinyNameBuffer = profile == null ? "" : profile.storytellerPortraitTinyName;
            promptBuffer = profile == null ? "" : profile.prompt;
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(760f, 620f); }
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

            float y = inRect.y + 42f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "DTO_ChatPersonaName".Translate());
            y += 26f;
            labelBuffer = Widgets.TextField(new Rect(inRect.x, y, inRect.width, 28f), labelBuffer ?? "");
            y += 36f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "DTO_ChatPersonaDescription".Translate());
            y += 26f;
            descriptionBuffer = Widgets.TextField(new Rect(inRect.x, y, inRect.width, 28f), descriptionBuffer ?? "");
            y += 36f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "DTO_StorytellerAppearanceTitle".Translate());
            y += 26f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "DTO_StorytellerLabel".Translate());
            y += 24f;
            storytellerLabelBuffer = Widgets.TextField(new Rect(inRect.x, y, inRect.width, 28f), storytellerLabelBuffer ?? "");
            y += 36f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "DTO_StorytellerDescription".Translate());
            y += 24f;
            storytellerDescriptionBuffer = Widgets.TextField(new Rect(inRect.x, y, inRect.width, 28f), storytellerDescriptionBuffer ?? "");
            y += 36f;

            float columnWidth = (inRect.width - 16f) / 3f;
            Widgets.Label(new Rect(inRect.x, y, columnWidth, 24f), "DTO_StorytellerPortraitFolder".Translate());
            Widgets.Label(new Rect(inRect.x + columnWidth + 8f, y, columnWidth, 24f), "DTO_StorytellerPortraitLargeName".Translate());
            Widgets.Label(new Rect(inRect.x + (columnWidth + 8f) * 2f, y, columnWidth, 24f), "DTO_StorytellerPortraitTinyName".Translate());
            y += 24f;
            portraitFolderBuffer = Widgets.TextField(new Rect(inRect.x, y, columnWidth, 28f), portraitFolderBuffer ?? "");
            portraitLargeNameBuffer = Widgets.TextField(new Rect(inRect.x + columnWidth + 8f, y, columnWidth, 28f), portraitLargeNameBuffer ?? "");
            portraitTinyNameBuffer = Widgets.TextField(new Rect(inRect.x + (columnWidth + 8f) * 2f, y, columnWidth, 28f), portraitTinyNameBuffer ?? "");
            y += 34f;

            OrcaChatPersonaProfile preview = new OrcaChatPersonaProfile
            {
                label = labelBuffer,
                description = descriptionBuffer,
                storytellerLabel = storytellerLabelBuffer,
                storytellerDescription = storytellerDescriptionBuffer,
                storytellerPortraitFolder = portraitFolderBuffer,
                storytellerPortraitLargeName = portraitLargeNameBuffer,
                storytellerPortraitTinyName = portraitTinyNameBuffer
            };
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "DTO_StorytellerPortraitLargePath".Translate() + ": " + OrcaStorytellerAppearance.LargePortraitPath(preview));
            y += 24f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "DTO_StorytellerPortraitTinyPath".Translate() + ": " + OrcaStorytellerAppearance.TinyPortraitPath(preview));
            y += 34f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "DTO_ChatPersonaPrompt".Translate());
            y += 26f;
            Rect promptOuter = new Rect(inRect.x, y, inRect.width, inRect.height - y - 48f);
            float viewHeight = Mathf.Max(promptOuter.height, Text.CalcHeight(promptBuffer ?? "", promptOuter.width - 20f) + 80f);
            Rect promptView = new Rect(0f, 0f, promptOuter.width - 16f, viewHeight);
            Widgets.BeginScrollView(promptOuter, ref promptScrollPosition, promptView);
            promptBuffer = Widgets.TextArea(new Rect(0f, 0f, promptView.width, viewHeight), promptBuffer ?? "");
            Widgets.EndScrollView();

            Rect saveRect = new Rect(inRect.xMax - 170f, inRect.yMax - 36f, 80f, 32f);
            if (Widgets.ButtonText(saveRect, "DTO_ChatPersonaSave".Translate()))
            {
                profile.label = labelBuffer.NullOrEmpty() ? "New Persona" : labelBuffer;
                profile.description = descriptionBuffer ?? "";
                profile.storytellerLabel = storytellerLabelBuffer ?? "";
                profile.storytellerDescription = storytellerDescriptionBuffer ?? "";
                profile.storytellerPortraitFolder = portraitFolderBuffer ?? "";
                profile.storytellerPortraitLargeName = portraitLargeNameBuffer ?? "";
                profile.storytellerPortraitTinyName = portraitTinyNameBuffer ?? "";
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
    }
}
