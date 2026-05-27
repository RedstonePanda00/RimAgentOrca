using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class StorytellerCompProperties_DeepseekOrca : StorytellerCompProperties
    {
        public float mtbDays = 4.8f;
        public float minSpacingDays = 2f;
        public FloatRange pointsFactorRange = new FloatRange(0.75f, 1.25f);

        public StorytellerCompProperties_DeepseekOrca()
        {
            compClass = typeof(StorytellerComp_DeepseekOrca);
        }

        public override IEnumerable<string> ConfigErrors(StorytellerDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }

            if (mtbDays <= 0f)
            {
                yield return "mtbDays must be greater than 0.";
            }

            if (minSpacingDays < 0f)
            {
                yield return "minSpacingDays cannot be negative.";
            }

            if (pointsFactorRange.min <= 0f || pointsFactorRange.max < pointsFactorRange.min)
            {
                yield return "pointsFactorRange must be positive and ordered.";
            }
        }
    }
}
