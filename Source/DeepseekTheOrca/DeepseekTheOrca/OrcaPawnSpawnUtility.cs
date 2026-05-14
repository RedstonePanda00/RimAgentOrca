using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class PawnSpawnRequest
    {
        public Map map;
        public Faction faction;
        public int count;
        public IntVec3 spawnCell;
        public int radius;
    }

    public static class OrcaPawnSpawnUtility
    {
        public static bool TryBuildRequest(AiToolContext context, Dictionary<string, string> arguments, out PawnSpawnRequest request, out string rejectReason)
        {
            request = null;
            rejectReason = null;

            Map map = context == null ? null : context.Map;
            if (map == null)
            {
                rejectReason = "no current map";
                return false;
            }

            string factionText = GetArg(arguments, "factionDef");
            if (factionText.NullOrEmpty())
            {
                rejectReason = "missing argument: factionDef";
                return false;
            }

            Faction faction = FindFaction(factionText);
            if (faction == null)
            {
                rejectReason = "faction not found: " + factionText;
                return false;
            }
            if (faction.def.pawnGroupMakers.NullOrEmpty())
            {
                rejectReason = "faction has no pawn group makers: " + faction.def.defName;
                return false;
            }

            int count = ParseInt(arguments, "count", 1);
            if (count < 1 || count > 50)
            {
                rejectReason = "count must be between 1 and 50";
                return false;
            }

            string spawnCellText = GetArg(arguments, "spawnCell");
            IntVec3 spawnCell;
            if (!TryParseCell(spawnCellText, out spawnCell))
            {
                rejectReason = "invalid spawnCell. Use x,z or x,y,z.";
                return false;
            }
            if (!spawnCell.InBounds(map))
            {
                rejectReason = "spawnCell is outside the current map: " + spawnCell;
                return false;
            }

            int radius = ParseInt(arguments, "radius", 5);
            if (radius < 0 || radius > 30)
            {
                rejectReason = "radius must be between 0 and 30";
                return false;
            }

            if (!TryFindSpawnCellNear(spawnCell, map, radius, out IntVec3 ignored))
            {
                rejectReason = "no valid walkable spawn cell near " + spawnCell + " within radius " + radius;
                return false;
            }

            request = new PawnSpawnRequest
            {
                map = map,
                faction = faction,
                count = count,
                spawnCell = spawnCell,
                radius = radius
            };
            return true;
        }

        public static bool TrySpawnPawns(AiToolContext context, Dictionary<string, string> arguments, out string message)
        {
            message = "";

            PawnSpawnRequest request;
            string rejectReason;
            if (!TryBuildRequest(context, arguments, out request, out rejectReason))
            {
                message = rejectReason;
                return false;
            }

            List<Pawn> spawned = new List<Pawn>();
            for (int i = 0; i < request.count; i++)
            {
                IntVec3 cell;
                if (!TryFindSpawnCellNear(request.spawnCell, request.map, request.radius, out cell))
                {
                    message = "failed to find spawn cell after spawning " + spawned.Count + " pawn(s)";
                    return spawned.Count > 0;
                }

                PawnKindDef kind = request.faction.RandomPawnKind();
                if (kind == null)
                {
                    message = "faction could not provide a random pawn kind: " + request.faction.def.defName;
                    return spawned.Count > 0;
                }

                Pawn pawn = PawnGenerator.GeneratePawn(kind, request.faction, request.map.Tile);
                GenSpawn.Spawn(pawn, cell, request.map);
                spawned.Add(pawn);
            }

            message = "spawned " + spawned.Count + " pawn(s) for " + request.faction.def.defName + " near " + request.spawnCell;
            return spawned.Count > 0;
        }

        private static bool TryFindSpawnCellNear(IntVec3 center, Map map, int radius, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            if (radius == 0)
            {
                if (center.Standable(map))
                {
                    result = center;
                    return true;
                }

                return false;
            }

            return CellFinder.TryFindRandomCellNear(center, map, radius, cell => cell.Standable(map), out result);
        }

        private static Faction FindFaction(string factionText)
        {
            if (Find.FactionManager == null || factionText.NullOrEmpty())
            {
                return null;
            }

            string needle = factionText.Trim();
            FactionDef factionDef = DefDatabase<FactionDef>.GetNamedSilentFail(needle);
            if (factionDef != null)
            {
                Faction byDef = Find.FactionManager.FirstFactionOfDef(factionDef);
                if (byDef != null)
                {
                    return byDef;
                }
            }

            return Find.FactionManager.AllFactionsListForReading.FirstOrDefault(faction =>
                faction != null
                && (string.Equals(faction.def.defName, needle, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(faction.Name, needle, System.StringComparison.OrdinalIgnoreCase)
                    || (!faction.Name.NullOrEmpty() && faction.Name.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0)));
        }

        private static bool TryParseCell(string text, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (text.NullOrEmpty())
            {
                return false;
            }

            string cleaned = text.Replace("(", "").Replace(")", "");
            string[] parts = cleaned.Split(',');
            if (parts.Length != 2 && parts.Length != 3)
            {
                return false;
            }

            int x;
            int z;
            if (!int.TryParse(parts[0].Trim(), out x))
            {
                return false;
            }

            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[1].Trim(), out z))
                {
                    return false;
                }
            }
            else if (!int.TryParse(parts[2].Trim(), out z))
            {
                return false;
            }

            cell = new IntVec3(x, 0, z);
            return true;
        }

        private static string GetArg(Dictionary<string, string> arguments, string key)
        {
            string value;
            return arguments != null && arguments.TryGetValue(key, out value) ? value : "";
        }

        private static int ParseInt(Dictionary<string, string> arguments, string key, int defaultValue)
        {
            string text = GetArg(arguments, key);
            int value;
            return !text.NullOrEmpty() && int.TryParse(text, out value) ? value : defaultValue;
        }
    }
}
