using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
    public static class PawnToolUtility
    {
        public static string PawnId(Pawn pawn)
        {
            return pawn == null ? "" : pawn.thingIDNumber.ToString();
        }

        public static string PawnName(Pawn pawn)
        {
            if (pawn == null)
            {
                return "";
            }

            return pawn.Name == null ? pawn.LabelShort : pawn.Name.ToStringFull;
        }

        public static string PawnType(Pawn pawn)
        {
            if (pawn == null)
            {
                return "";
            }
            if (pawn.IsFreeColonist)
            {
                return "freeColonist";
            }
            if (pawn.IsSlaveOfColony)
            {
                return "slaveOfColony";
            }
            if (pawn.IsPrisonerOfColony)
            {
                return "prisonerOfColony";
            }
            if (pawn.IsColonyMech)
            {
                return "colonyMech";
            }
            if (pawn.IsColonyAnimal)
            {
                return "colonyAnimal";
            }
            if (pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer))
            {
                return "hostile";
            }
            return "other";
        }

        public static string RaceSummary(Pawn pawn)
        {
            if (pawn == null || pawn.def == null)
            {
                return "";
            }

            string label = pawn.def.label.NullOrEmpty() ? pawn.def.defName : pawn.def.label;
            return label + "[" + pawn.def.defName + "]";
        }

        public static string RaceDescription(Pawn pawn)
        {
            if (pawn == null || pawn.def == null)
            {
                return "";
            }

            return pawn.def.description ?? "";
        }

        public static string FactionName(Faction faction)
        {
            if (faction == null)
            {
                return "none";
            }

            return faction.Name.NullOrEmpty() ? faction.def.defName : faction.Name;
        }

        public static Pawn FindPawn(Map map, string pawnIdOrName)
        {
            if (map == null || map.mapPawns == null || pawnIdOrName.NullOrEmpty())
            {
                return null;
            }

            string needle = pawnIdOrName.Trim();
            int id;
            if (int.TryParse(needle, out id))
            {
                Pawn byNumber = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn != null && pawn.thingIDNumber == id);
                if (byNumber != null)
                {
                    return byNumber;
                }
            }

            Pawn exact = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn != null && MatchesPawnText(pawn, needle, exactMatch: true));
            if (exact != null)
            {
                return exact;
            }

            return map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn != null && MatchesPawnText(pawn, needle, exactMatch: false));
        }

        private static bool MatchesPawnText(Pawn pawn, string needle, bool exactMatch)
        {
            List<string> values = new List<string>
            {
                pawn.ThingID,
                pawn.GetUniqueLoadID(),
                pawn.LabelShort,
                pawn.LabelNoCount,
                PawnName(pawn)
            };

            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                if (value.NullOrEmpty())
                {
                    continue;
                }

                if (exactMatch)
                {
                    if (string.Equals(value, needle, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                else if (value.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
