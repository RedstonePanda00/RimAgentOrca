using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using RimAgent.Core;
using RimWorld;
using Verse;

namespace RimAgent;

internal sealed partial class OrcaAgentGameAdapter
{
    private int? stepUntil;
    private Game stepOwner;
    private int stepRaidCount;
    private bool stepStopOnThreat;
    private string stepStopReason;
    private readonly HashSet<int> stepHostiles = new();
    public object Read(string tool, JsonObject a) => tool switch {
        "rimagent_letters_list" => Letters(a),
        "rimagent_letter_get" => LetterDetails(a),
        "rimagent_quests_list" => Quests(a),
        "rimagent_quest_get" => QuestDetails(a),
        "rimagent_config_get" => OrcaExternalConfiguration.Read(),
        "rimagent_dialogs_list" => Dialogs(),
        "rimagent_visual_watch_get" => VisualWatch(a),
        "rimagent_pawn_orders" => PawnOrders(a),
        "rimagent_pawn_get" => PawnDetails(a),
        "rimagent_trade_get" => TradeRead(a),
        "rimagent_storyteller_get" or "rimagent_incidents_list" => StoryRead(tool, a),
        "rimagent_gemini_hiss_get" => GeminiHissService.ExternalStatus(a.Number("mapId", 0, int.MaxValue)),
        "rimagent_definitions" => Definitions(a),
        "rimagent_map_inspect" => InspectMap(a),
        "rimagent_things_list" => Things(a),
        "rimagent_thing_get" => ThingView(Thing(a)),
        "rimagent_gizmos_list" => Gizmos(a),
        "rimagent_build_check" => Build(a, false),
        "rimagent_zones_list" => Page(Map(a.Number("mapId", 0, int.MaxValue)).zoneManager.AllZones.OrderBy(z => z.ID).Select(ZoneView), a),
        "rimagent_recipes_list" => Page(Bench(a).def.AllRecipes.Where(r => r.AvailableNow).OrderBy(r => r.defName).Select(r => (object)new {
            defName = r.defName, label = r.LabelCap.ToString(), products = r.products.Select(p => new { defName = p.thingDef.defName, p.count }),
            requiredWorkType = r.requiredGiverWorkType?.defName, workSkill = r.workSkill?.defName, workAmount = r.workAmount,
            ingredients = r.ingredients.Select(i => new { description = i.ToString(), allowedDefs = i.filter.AllowedThingDefs.Select(d => d.defName).Take(100).ToArray() }),
            skills = r.skillRequirements?.Select(s => new { skill = s.skill.defName, s.minLevel }) }), a),
        "rimagent_bills_list" => Bills(a),
        "rimagent_research_list" => Page(DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(r => Matches(r, a)).OrderBy(r => r.defName).Select(ResearchView), a),
        "rimagent_work_get" => WorkView(Pawn(a)),
        _ => throw new BridgeException("unknown_tool", tool)
    };
    private static object Page(IEnumerable<object> source, JsonObject a)
    {
        int offset = a.Number("offset", 0, 100000, 0), limit = a.Number("limit", 1, 100, 40);
        var rows = source.Skip(offset).Take(limit + 1).ToArray();
        return new { items = rows.Take(limit).ToArray(), offset, nextOffset = rows.Length > limit ? (int?)(offset + limit) : null };
    }
    private static bool Matches(Def def, JsonObject a) => a["query"] == null || def.defName.Contains(a.Text("query", 128), StringComparison.OrdinalIgnoreCase) || (def.label?.Contains(a.Text("query", 128), StringComparison.OrdinalIgnoreCase) ?? false);
    private static T Def<T>(string name) where T : Def => DefDatabase<T>.GetNamedSilentFail(name) ?? throw new BridgeException("def_not_found", typeof(T).Name + ": " + name);
    private static Thing Thing(JsonObject a)
    {
        var map = Map(a.Number("mapId", 0, int.MaxValue));
        var id = a.Text("thingId", 128);
        return map.listerThings.AllThings.FirstOrDefault(t => t.GetUniqueLoadID() == id && t.Spawned && !t.Position.Fogged(map))
            ?? throw new BridgeException("thing_not_found", "Visible spawned thing not found on this map.");
    }
    private static Pawn Pawn(JsonObject a)
    {
        var id = a.Text("pawnId", 128);
        var pawn = Map(a.Number("mapId", 0, int.MaxValue)).mapPawns.FreeColonistsSpawned.FirstOrDefault(p => p.GetUniqueLoadID() == id);
        return pawn ?? throw new BridgeException("pawn_not_found", "Spawned free colonist not found.");
    }
    private static Building_WorkTable Bench(JsonObject a)
    {
        var thing = Thing(a);
        if (thing is not Building_WorkTable bench || thing.Faction != Faction.OfPlayer) throw new BridgeException("not_workbench", "Select a player-owned production worktable.");
        return bench;
    }
    private static IntVec3 Cell(Map map, JsonObject a) => new(a.Number("x", 0, map.Size.x - 1), 0, a.Number("z", 0, map.Size.z - 1));
    private static CellRect Rect(Map map, JsonObject a, int max)
    {
        var c = Cell(map, a);
        int w = a.Number("width", 1, 32), h = a.Number("height", 1, 32);
        if (w * h > max || c.x + w > map.Size.x || c.z + h > map.Size.z) throw new BridgeException("invalid_area", "Rectangle exceeds map or cell limit.");
        return new CellRect(c.x, c.z, w, h);
    }
    private static object Definitions(JsonObject a)
    {
        var kind = a.Text("kind", 32);
        IEnumerable<Def> defs = kind switch {
            "build" => DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.blueprintDef != null && d.designationCategory != null),
            "terrain" => DefDatabase<TerrainDef>.AllDefsListForReading.Where(d => d.blueprintDef != null && d.designationCategory != null),
            "stuff" => DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsStuff),
            "plant" => DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.plant?.Sowable == true),
            "work" => DefDatabase<WorkTypeDef>.AllDefsListForReading,
            _ => throw new BridgeException("invalid_arguments", "kind must be build, terrain, stuff, plant or work.")
        };
        return Page(defs.Where(d => Matches(d, a)).OrderBy(d => d.defName).Select(d => {
            var b = d as BuildableDef; var t = d as ThingDef;
            return (object)new { d.defName, label = d.LabelCap.ToString(), description = d.description,
                size = b == null ? null : new { x = b.Size.x, z = b.Size.z },
                costs = b?.costList?.Select(c => new { defName = c.thingDef.defName, c.count }), stuffCount = b?.costStuffCount,
                allowedStuff = t?.MadeFromStuff == true ? GenStuff.AllowedStuffsFor(t).Select(s => s.defName).ToArray() : null,
                research = b?.researchPrerequisites?.Select(r => new { r.defName, finished = r.IsFinished }),
                fertilityMin = t?.plant?.fertilityMin };
        }), a);
    }
    private static object InspectMap(JsonObject a)
    {
        var map = Map(a.Number("mapId", 0, int.MaxValue));
        var area = Rect(map, a, 1024);
        return new { mapId = map.uniqueID, tick = Find.TickManager.TicksGame, cells = area.Cells.Select(c => c.Fogged(map)
            ? (object)new { x = c.x, z = c.z, fogged = true }
            : new { x = c.x, z = c.z, fogged = false, terrain = c.GetTerrain(map).defName, fertility = c.GetFertility(map), temperatureCelsius = c.GetTemperature(map), walkable = c.Walkable(map), roof = c.GetRoof(map)?.defName,
                things = c.GetThingList(map).Select(t => new { thingId = t.GetUniqueLoadID(), defName = t.def.defName }).ToArray(),
                designations = map.designationManager.AllDesignationsAt(c).Select(d => d.def.defName).ToArray() }).ToArray() };
    }
    private static object Things(JsonObject a)
    {
        var map = Map(a.Number("mapId", 0, int.MaxValue)); var kind = a.Text("kind", 32);
        if (kind is not ("building" or "item" or "blueprint" or "frame" or "plant" or "pawn")) throw new BridgeException("invalid_arguments", "Unknown thing kind.");
        return Page(map.listerThings.AllThings.Where(t => t.Spawned && !t.Position.Fogged(map) && (a["defName"] == null || t.def.defName == a.Text("defName", 128)) && (a["playerOwned"] == null || (t.Faction == Faction.OfPlayer) == a.Flag("playerOwned")))
            .Where(t => kind switch { "building" => t is Building, "item" => t.def.category == ThingCategory.Item, "blueprint" => t is Blueprint, "frame" => t is Frame, "plant" => t is Plant, _ => t is Pawn })
            .OrderBy(t => t.thingIDNumber).Select(ThingView), a);
    }
    private static object ThingView(Thing t) => new {
        thingId = t.GetUniqueLoadID(), defName = t.def.defName, label = t.LabelCap.ToString(), x = t.Position.x, z = t.Position.z,
        rotation = t.Rotation.AsInt, stackCount = t.stackCount, playerOwned = t.Faction == Faction.OfPlayer,
        faction = t.Faction?.Name, hostile = t.HostileTo(Faction.OfPlayer),
        pawn = t is Pawn pawn ? new { downed = pawn.Downed, drafted = pawn.Drafted, trader = pawn.TraderKind?.defName, canTrade = pawn.CanTradeNow } : null,
        forbidden = t.IsForbidden(Faction.OfPlayer), hitPoints = t.HitPoints, maxHitPoints = t.MaxHitPoints,
        buildDef = t.def.entityDefToBuild?.defName, stuff = t.Stuff?.defName,
        totalMaterialCost = (t as IConstructible)?.TotalMaterialCost().Select(c => new { defName = c.thingDef.defName, c.count }).ToArray(),
        powerOn = t.TryGetComp<CompPowerTrader>()?.PowerOn, fuel = t.TryGetComp<CompRefuelable>()?.Fuel,
        targetTemperatureCelsius = t.TryGetComp<CompTempControl>()?.TargetTemperature,
        plant = t is Plant p ? new { growth = p.Growth, isTree = p.def.plant.IsTree, harvestableNow = p.HarvestableNow, harvestTag = p.def.plant.harvestTag } : null,
        inspect = t.GetInspectString(), designations = t.Map.designationManager.AllDesignationsOn(t).Select(d => d.def.defName).ToArray(),
        cellDesignations = t.Map.designationManager.AllDesignationsAt(t.Position).Select(d => d.def.defName).ToArray()
    };
    private static object ResearchView(ResearchProjectDef r) => new {
        r.defName, label = r.LabelCap.ToString(), r.description, finished = r.IsFinished, canStart = r.CanStartNow,
        selected = Find.ResearchManager.IsCurrentProject(r), progress = Find.ResearchManager.GetProgress(r), cost = r.CostApparent,
        prerequisites = r.prerequisites?.Select(p => new { p.defName, finished = p.IsFinished }).ToArray(), bench = r.requiredResearchBuilding?.defName
    };
    private static object WorkView(Pawn p) => new { pawnId = p.GetUniqueLoadID(), manualPriorities = Current.Game.playSettings.useWorkPriorities,
        skills = p.skills?.skills.Select(s => new { defName = s.def.defName, level = s.Level, disabled = s.TotallyDisabled, passion = s.passion.ToString() }).ToArray(),
        work = DefDatabase<WorkTypeDef>.AllDefsListForReading.Select(w => new { w.defName, label = w.labelShort, disabled = p.WorkTypeIsDisabled(w), priority = p.workSettings?.GetPriority(w) ?? 0 }).ToArray() };
    private static object Bills(JsonObject a)
    {
        var bench = Bench(a);
        return new { thingId = bench.GetUniqueLoadID(), usable = bench.CurrentlyUsableForBills(), burning = bench.IsBurning(),
            interactionCell = new { x = bench.InteractionCell.x, z = bench.InteractionCell.z, standable = bench.InteractionCell.Standable(bench.Map) },
            workGivers = DefDatabase<WorkGiverDef>.AllDefsListForReading.Where(w => w.fixedBillGiverDefs?.Contains(bench.def) == true)
                .Select(w => new { w.defName, workType = w.workType?.defName }).ToArray(),
            bills = bench.BillStack.Bills.OfType<Bill_Production>().Select(BillView).ToArray() };
    }
    private static object BillView(Bill_Production b) => new { billId = b.GetUniqueLoadID(), recipeDef = b.recipe.defName, repeatMode = b.repeatMode.defName,
        count = b.repeatCount, targetCount = b.targetCount, suspended = b.suspended, ingredientRadius = b.ingredientSearchRadius,
        allowedDefs = b.ingredientFilter.AllowedThingDefs.Select(d => d.defName).ToArray(), storeMode = b.GetStoreMode().defName,
        allowedSkill = new { min = b.allowedSkillRange.min, max = b.allowedSkillRange.max }, pawnRestriction = b.PawnRestriction?.GetUniqueLoadID(),
        unfinished = (b as Bill_ProductionWithUft)?.BoundUft is { } u ? new { thingId = u.GetUniqueLoadID(), creatorId = u.Creator?.GetUniqueLoadID(), creatorJob = u.Creator?.CurJob?.def.defName, creatorDowned = u.Creator?.Downed, creatorDrafted = u.Creator?.Drafted, spawned = u.Spawned } : null,
        workSkill = b.recipe.workSkill?.defName, skillRequirements = b.recipe.skillRequirements?.Select(s => new { skill = s.skill.defName, s.minLevel }).ToArray() };
    private static object ZoneView(Zone z) => new { zoneId = z.GetUniqueLoadID(), kind = z is Zone_Growing ? "growing" : z is Zone_Stockpile ? "stockpile" : "other",
        label = z.label, cells = z.Cells.Select(c => new { x = c.x, z = c.z }).ToArray(),
        plantDef = (z as Zone_Growing)?.PlantDefToGrow.defName, allowSow = (z as Zone_Growing)?.allowSow, allowCut = (z as Zone_Growing)?.allowCut,
        priority = z is Zone_Stockpile s ? (int?)s.GetStoreSettings().Priority : null,
        allowedDefs = z is Zone_Stockpile stock ? stock.GetStoreSettings().filter.AllowedThingDefs.Select(d => d.defName).ToArray() : null };
}
