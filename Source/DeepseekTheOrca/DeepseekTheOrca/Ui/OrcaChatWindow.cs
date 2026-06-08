using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaChatWindowManager
    {
        private static readonly OrcaChatSession session = new OrcaChatSession();

        public static OrcaChatSession Session
        {
            get { return session; }
        }

        public static bool IsOpen
        {
            get { return Find.WindowStack != null && Find.WindowStack.IsOpen(typeof(OrcaChatWindow)); }
        }

        public static void Toggle()
        {
            if (Find.WindowStack == null)
            {
                return;
            }

            if (IsOpen)
            {
                Find.WindowStack.TryRemove(typeof(OrcaChatWindow));
            }
            else
            {
                Find.WindowStack.Add(new OrcaChatWindow());
            }
        }

        public static void Open()
        {
            if (Find.WindowStack != null && !IsOpen)
            {
                Find.WindowStack.Add(new OrcaChatWindow());
            }
        }
    }

    public sealed class OrcaChatWindow : Window
    {
        private const string InputControlName = "DTO_OrcaChatInput";
        private Vector2 messageScrollPosition;
        private string inputBuffer = "";
        private int displayedConversationVersion = -1;

        public OrcaChatWindow()
        {
            doWindowBackground = false;
            doCloseX = false;
            closeOnAccept = false;
            closeOnCancel = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            draggable = true;
            resizeable = true;
            drawShadow = true;
            shadowAlpha = DeepseekTheOrcaMod.Settings == null ? 0.82f : Mathf.Clamp01(DeepseekTheOrcaMod.Settings.chatWindowAlpha);
            onlyOneOfTypeAllowed = true;
        }

        public override Vector2 InitialSize
        {
            get
            {
                OrcaChatWindowContext context = new OrcaChatWindowContext(OrcaChatWindowManager.Session, Rect.zero, Rect.zero, Rect.zero, 1f);
                return new Vector2(560f + OrcaExtensionManager.RequestedExtraWidth(context), 260f);
            }
        }

        protected override void SetInitialSizeAndPosition()
        {
            Vector2 initialSize = InitialSize;
            float x = Mathf.Clamp(80f, 0f, Mathf.Max(0f, UI.screenWidth - initialSize.x));
            float y = Mathf.Clamp(80f, 0f, Mathf.Max(0f, UI.screenHeight - initialSize.y));
            windowRect = new Rect(x, y, initialSize.x, initialSize.y).Rounded();
        }

        protected override float Margin
        {
            get { return 0f; }
        }

        public override void DoWindowContents(Rect inRect)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            float alpha = settings == null ? 0.82f : Mathf.Clamp01(settings.chatWindowAlpha);
            shadowAlpha = alpha;
            Widgets.DrawBoxSolid(inRect, new Color(0f, 0f, 0f, alpha));
            DrawFixedBorder(inRect);

            OrcaChatWindowContext measureContext = new OrcaChatWindowContext(OrcaChatWindowManager.Session, inRect, inRect, Rect.zero, alpha);
            float extensionWidth = Mathf.Min(
                OrcaExtensionManager.RequestedExtraWidth(measureContext),
                Mathf.Max(0f, inRect.width - 360f));
            float extensionGap = extensionWidth > 0f ? 8f : 0f;
            Rect chatRect = new Rect(inRect.x, inRect.y, inRect.width - extensionWidth - extensionGap, inRect.height);
            Rect extensionRect = extensionWidth > 0f
                ? new Rect(chatRect.xMax + extensionGap, inRect.y, extensionWidth, inRect.height)
                : Rect.zero;

            if (extensionWidth > 0f)
            {
                Widgets.DrawBoxSolid(new Rect(chatRect.xMax + extensionGap * 0.5f, inRect.y + 8f, 1f, inRect.height - 16f), new Color(0.55f, 0.55f, 0.55f, 0.75f));
            }

            Rect contentRect = chatRect.ContractedBy(10f);
            float y = 0f;

            Text.Font = GameFont.Small;
            Rect messagesRect = new Rect(contentRect.x, contentRect.y + y, contentRect.width, contentRect.height - y - 96f);
            DrawMessages(messagesRect);
            y += messagesRect.height + 8f;

            Rect inputRect = new Rect(contentRect.x, contentRect.y + y, contentRect.width, 64f);
            HandleEnterToSend();
            GUI.SetNextControlName(InputControlName);
            inputBuffer = Widgets.TextArea(inputRect, inputBuffer);
            y += 70f;

            OrcaChatWindowContext drawContext = new OrcaChatWindowContext(OrcaChatWindowManager.Session, inRect, chatRect, extensionRect, alpha);
            if (extensionWidth > 0f)
            {
                OrcaExtensionManager.DrawRightExtensions(extensionRect.ContractedBy(8f), drawContext);
            }
            OrcaExtensionManager.DrawOverlays(inRect, drawContext);
            DrawCloseButton(inRect);

            OrcaChatWindowManager.Session.Tick();
        }

        private void DrawCloseButton(Rect rect)
        {
            Rect buttonRect = new Rect(rect.xMax - 20f, rect.y + 6f, 18f, 18f);
            Rect imageRect = new Rect(buttonRect.x + 4.5f, buttonRect.y + 4.5f, 9f, 9f);
            GUI.DrawTexture(imageRect, TexButton.CloseXSmall);
            if (Widgets.ButtonInvisible(buttonRect))
            {
                Close();
            }
        }

        private static void DrawFixedBorder(Rect rect)
        {
            Color borderColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 1f), borderColor);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), borderColor);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 1f, rect.height), borderColor);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), borderColor);
        }

        private void DrawMessages(Rect outRect)
        {
            string text = OrcaChatWindowManager.Session.DisplayText;
            if (text.NullOrEmpty())
            {
                return;
            }

            Rect innerRect = new Rect(outRect.x + 6f, outRect.y + 6f, outRect.width - 12f, outRect.height - 12f);
            float viewWidth = Mathf.Max(10f, innerRect.width - 16f);
            float viewHeight = Mathf.Max(innerRect.height, Text.CalcHeight(text, viewWidth) + 8f);

            if (displayedConversationVersion != OrcaChatWindowManager.Session.ConversationVersion)
            {
                displayedConversationVersion = OrcaChatWindowManager.Session.ConversationVersion;
                messageScrollPosition.y = Mathf.Max(0f, viewHeight - innerRect.height);
            }

            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
            Widgets.BeginScrollView(innerRect, ref messageScrollPosition, viewRect);
            Widgets.Label(new Rect(0f, 0f, viewWidth, viewHeight), text);
            Widgets.EndScrollView();
        }

        private void HandleEnterToSend()
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.KeyDown)
            {
                return;
            }

            if (GUI.GetNameOfFocusedControl() != InputControlName)
            {
                return;
            }

            if (!IsSendEnterEvent(current))
            {
                return;
            }

            bool hasText = !inputBuffer.NullOrEmpty();
            LogDebug("Chat input Enter detected: keyCode=" + current.keyCode
                + ", character=" + FormatEventCharacter(current.character)
                + ", shift=" + current.shift
                + ", waiting=" + OrcaChatWindowManager.Session.IsWaiting
                + ", hasText=" + hasText
                + ", focusedControl=" + GUI.GetNameOfFocusedControl());

            if (current.shift)
            {
                return;
            }

            if (!OrcaChatWindowManager.Session.IsWaiting && hasText)
            {
                SendInputBuffer();
            }

            current.Use();
        }

        private static bool IsSendEnterEvent(Event current)
        {
            return current.keyCode == KeyCode.Return
                || current.keyCode == KeyCode.KeypadEnter
                || current.character == '\n'
                || current.character == '\r';
        }

        private static string FormatEventCharacter(char character)
        {
            if (character == '\n')
            {
                return "\\n";
            }
            if (character == '\r')
            {
                return "\\r";
            }
            if (character == '\0')
            {
                return "\\0";
            }

            return character + " (" + ((int)character) + ")";
        }

        private static void LogDebug(string message)
        {
            if (DeepseekTheOrcaMod.Settings != null && DeepseekTheOrcaMod.Settings.debugLogging)
            {
                Log.Message("[RimAgent] " + message);
            }
        }

        private void SendInputBuffer()
        {
            string text = inputBuffer == null ? "" : inputBuffer.TrimEnd('\r', '\n');
            if (text.NullOrEmpty())
            {
                inputBuffer = "";
                return;
            }

            OrcaChatWindowManager.Session.Send(text);
            inputBuffer = "";
            displayedConversationVersion = -1;
            GUI.FocusControl(null);
        }
    }
}