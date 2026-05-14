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

        public CachedIncidentDef(IncidentDef incidentDef)
        {
            Def = incidentDef;
            DefName = incidentDef.defName;
            Label = incidentDef.LabelCap.ToString();
            Category = incidentDef.category == null ? "" : incidentDef.category.defName;
            ModName = incidentDef.modContentPack == null ? "" : incidentDef.modContentPack.Name;
            HasQuestScript = incidentDef.questScriptDef != null;
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
}
