using UnityEngine;

namespace DeepseekTheOrca
{
    public abstract class OrcaSettingsTab
    {
        public abstract string Id { get; }
        public abstract string Label { get; }
        public virtual int Order { get { return 0; } }

        public virtual bool Visible(OrcaSettingsContext context)
        {
            return true;
        }

        public virtual void OnSelected(OrcaSettingsContext context)
        {
        }

        public abstract void Draw(Rect rect, OrcaSettingsContext context);
    }
}
