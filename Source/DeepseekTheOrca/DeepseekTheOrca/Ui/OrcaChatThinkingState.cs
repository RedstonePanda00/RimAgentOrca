using System;
using System.Collections.Generic;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaChatThinkingState
    {
        private const int AnimationIntervalTicks = 30;
        private OrcaChatLine line;

        public string CurrentText()
        {
            int frame = (Find.TickManager == null ? 0 : Find.TickManager.TicksGame / AnimationIntervalTicks) % 3;
            return "Thinking" + new string('.', frame + 1);
        }

        public OrcaChatLine Ensure(List<OrcaChatLine> displayLines, string speaker, Action changed)
        {
            if (line != null)
            {
                UpdateText(changed);
                return line;
            }

            line = new OrcaChatLine(speaker, CurrentText());
            displayLines.Add(line);
            NotifyChanged(changed);
            return line;
        }

        public void Tick(Action changed)
        {
            if (line == null)
            {
                return;
            }

            UpdateText(changed);
        }

        public OrcaChatLine Consume()
        {
            OrcaChatLine current = line;
            line = null;
            return current;
        }

        public void ForgetIfCurrent(OrcaChatLine candidate)
        {
            if (candidate != null && candidate == line)
            {
                line = null;
            }
        }

        public void Clear()
        {
            line = null;
        }

        private void UpdateText(Action changed)
        {
            string text = CurrentText();
            if (line.Text == text)
            {
                return;
            }

            line.Text = text;
            NotifyChanged(changed);
        }

        private static void NotifyChanged(Action changed)
        {
            if (changed != null)
            {
                changed();
            }
        }
    }
}
