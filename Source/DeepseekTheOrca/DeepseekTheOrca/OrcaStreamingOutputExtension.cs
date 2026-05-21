using System;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaStreamingOutputExtensionWorker : OrcaExtensionWorker
    {
        private string charsPerSecondBuffer;

        public override OrcaReplyDisplayController CreateReplyDisplayController(string fullText, OrcaChatSession session)
        {
            if (fullText.NullOrEmpty())
            {
                return null;
            }

            return new OrcaTypewriterReplyDisplayController(fullText, CharsPerSecond());
        }

        public override void DrawSettings(Rect rect)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null)
            {
                return;
            }

            if (charsPerSecondBuffer == null)
            {
                charsPerSecondBuffer = settings.streamingOutputCharsPerSecond.ToString();
            }

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);
            listing.Label("DTO_StreamingOutputSettings".Translate());
            listing.TextFieldNumericLabeled(
                "DTO_StreamingOutputCharsPerSecond".Translate(),
                ref settings.streamingOutputCharsPerSecond,
                ref charsPerSecondBuffer,
                5f,
                200f);
            settings.streamingOutputCharsPerSecond = Mathf.Clamp(settings.streamingOutputCharsPerSecond, 5f, 200f);
            listing.Label("DTO_StreamingOutputSettingsNote".Translate());
            listing.End();
        }

        private static float CharsPerSecond()
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            return settings == null ? 45f : Mathf.Clamp(settings.streamingOutputCharsPerSecond, 5f, 200f);
        }
    }

    internal sealed class OrcaTypewriterReplyDisplayController : OrcaReplyDisplayController
    {
        private readonly string fullText;
        private readonly float charsPerSecond;
        private float visibleChars;

        public OrcaTypewriterReplyDisplayController(string fullText, float charsPerSecond)
        {
            this.fullText = fullText ?? "";
            this.charsPerSecond = Mathf.Clamp(charsPerSecond, 5f, 200f);
        }

        public override string VisibleText
        {
            get
            {
                int count = Mathf.Clamp((int)Math.Floor(visibleChars), 0, fullText.Length);
                return count >= fullText.Length ? fullText : fullText.Substring(0, count);
            }
        }

        public override bool IsComplete
        {
            get { return visibleChars >= fullText.Length; }
        }

        public override void Tick()
        {
            if (IsComplete)
            {
                return;
            }

            float delta = Time.unscaledDeltaTime;
            if (delta <= 0f)
            {
                delta = 1f / 60f;
            }

            visibleChars = Mathf.Min(fullText.Length, visibleChars + charsPerSecond * Mathf.Min(delta, 0.1f));
        }

        public override void Finish()
        {
            visibleChars = fullText.Length;
        }
    }
}
