using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class CachedIncidentDef
    {
        public readonly IncidentDef Def;
        public readonly string DefName;
        public readonly string Label;
        public readonly string Category;
        public readonly string ModName;
        public readonly bool HasQuestScript;
        public readonly string Polarity;
        public readonly int BudgetHint;

        public CachedIncidentDef(IncidentDef incidentDef)
        {
            Def = incidentDef;
            DefName = incidentDef.defName;
            Label = incidentDef.LabelCap.ToString();
            Category = incidentDef.category == null ? "" : incidentDef.category.defName;
            ModName = incidentDef.modContentPack == null ? "" : incidentDef.modContentPack.Name;
            HasQuestScript = incidentDef.questScriptDef != null;
            Polarity = OrcaIncidentPolarityClassifier.PolarityFor(incidentDef, DefName, Label, Category);
            BudgetHint = OrcaIncidentPolarityClassifier.BudgetHintFor(Polarity);
        }

        public string Summary
        {
            get
            {
                string result = DefName + " (" + Category + ", " + Label + ")";
                if (!ModName.NullOrEmpty())
                {
                    result += " [" + ModName + "]";
                }
                if (HasQuestScript)
                {
                    result += " quest";
                }
                result += " polarity=" + Polarity + " budgetHint=" + BudgetHint;
                return result;
            }
        }
    }

    public static class OrcaIncidentDefCache
    {
        private static List<CachedIncidentDef> cachedIncidents;
        private static Dictionary<string, CachedIncidentDef> cachedByDefName;

        public static IEnumerable<CachedIncidentDef> All
        {
            get
            {
                EnsureCached();
                return cachedIncidents;
            }
        }

        public static bool TryGet(string defName, out CachedIncidentDef cached)
        {
            EnsureCached();
            if (defName.NullOrEmpty())
            {
                cached = null;
                return false;
            }

            return cachedByDefName.TryGetValue(defName, out cached);
        }

        public static IEnumerable<CachedIncidentDef> AvailableFor(AiToolContext context)
        {
            EnsureCached();
            for (int i = 0; i < cachedIncidents.Count; i++)
            {
                CachedIncidentDef cached = cachedIncidents[i];
                IncidentDef incidentDef = cached.Def;
                if (!incidentDef.TargetAllowed(context.target))
                {
                    continue;
                }

                IncidentParms parms = StorytellerUtility.DefaultParmsNow(incidentDef.category, context.target);
                if (parms != null && incidentDef.Worker.CanFireNow(parms))
                {
                    yield return cached;
                }
            }
        }

        public static void Reset()
        {
            cachedIncidents = null;
            cachedByDefName = null;
        }

        private static void EnsureCached()
        {
            if (cachedIncidents != null)
            {
                return;
            }

            cachedIncidents = DefDatabase<IncidentDef>.AllDefsListForReading
                .Where(IsCacheable)
                .Select(def => new CachedIncidentDef(def))
                .OrderBy(def => def.Category)
                .ThenBy(def => def.DefName)
                .ToList();

            cachedByDefName = new Dictionary<string, CachedIncidentDef>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cachedIncidents.Count; i++)
            {
                cachedByDefName[cachedIncidents[i].DefName] = cachedIncidents[i];
            }
        }

        private static bool IsCacheable(IncidentDef incidentDef)
        {
            return incidentDef != null
                && !incidentDef.hidden
                && !incidentDef.defName.NullOrEmpty()
                && incidentDef.category != null
                && incidentDef.targetTags != null
                && incidentDef.targetTags.Count > 0
                && incidentDef.workerClass != null;
        }
    }

    public static class OrcaIncidentPolarityClassifier
    {
        public const string NegativeMajor = "negative_major";
        public const string NegativeMinor = "negative_minor";
        public const string Positive = "positive";
        public const string Neutral = "neutral";

        public static string PolarityFor(IncidentDef incidentDef, string defName, string label, string category)
        {
            string text = ((defName ?? "") + " " + (label ?? "") + " " + (category ?? "")).ToLowerInvariant();

            if (ContainsAny(text, "threatbig", "raidenemy", "raidfriendly", "manhunter", "infestation", "mech", "siege", "breach", "assault"))
            {
                return NegativeMajor;
            }

            if (ContainsAny(text, "disease", "toxic", "psychic", "shortcircuit", "solarflare", "cold snap", "heat wave", "eclipse", "zzzt", "threatsmall", "mad animal", "blight"))
            {
                return NegativeMinor;
            }

            if (ContainsAny(text, "trader", "visitor", "wanderer", "refugee", "quest", "shipchunk", "transportpod", "resource", "meteorite", "farm animals", "join"))
            {
                return Positive;
            }

            if (incidentDef != null && incidentDef.questScriptDef != null)
            {
                return Positive;
            }

            return Neutral;
        }

        public static int BudgetHintFor(string polarity)
        {
            if (polarity == NegativeMajor)
            {
                return -2;
            }
            if (polarity == NegativeMinor)
            {
                return -1;
            }
            if (polarity == Positive)
            {
                return 1;
            }

            return 0;
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (text.Contains(needles[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
