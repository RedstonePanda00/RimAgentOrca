using System;
using System.Linq;
using System.Text.Json.Nodes;
using RimAgent.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimAgent;

internal sealed partial class OrcaAgentGameAdapter
{
    public object Act(string tool, JsonObject a) => tool switch {
        "rimagent_letter_open" => OpenLetter(a),
        "rimagent_quest_open" => OpenQuest(a),
        "rimagent_quest_accept" => AcceptQuest(a),
        "rimagent_config_update" => OrcaExternalConfiguration.Update(a),
        "rimagent_dialog_choose" => ChooseDialog(a),
        "rimagent_dialog_name" => NameDialog(a),
        "rimagent_dialog_close" => CloseDialog(a),
        "rimagent_visual_watch_set" => SetVisualWatch(a),
        "rimagent_pawn_order" => ChoosePawnOrder(a),
        "rimagent_medical_set" => MedicalCare(a),
        "rimagent_trade_set" or "rimagent_trade_commit" or "rimagent_trade_cancel" => TradeAct(tool,a),
        "rimagent_gemini_hiss_begin" => BeginHiss(a),
        "rimagent_gemini_appease" => AppeaseGemini(a),
        "rimagent_gemini_hiss_complete" => OrcaPlanningWorkforce.Complete(GeminiHissService.State?.planner, a.Number("mapId", 0, int.MaxValue), a.Text("messageId", 64), a["plan"] as JsonObject),
        "rimagent_story_plan_apply" => ApplyStory(a),
        "rimagent_narration_post" => PostNarration(a),
        "rimagent_build_place" => Build(a, true),
        "rimagent_build_cancel" => CancelBuild(a),
        "rimagent_gizmo_invoke" => InvokeGizmo(a),
        "rimagent_gizmo_target" => TargetGizmo(a),
        "rimagent_designate" => Designate(a),
        "rimagent_zone_create" => CreateZone(a),
        "rimagent_zone_update" => UpdateZone(a),
        "rimagent_zone_delete" => DeleteZone(a),
        "rimagent_bill_create" or "rimagent_bill_update" or "rimagent_bill_delete" => EditBill(tool, a),
        "rimagent_research_select" or "rimagent_research_stop" => Research(tool, a),
        "rimagent_work_set" => SetWork(a),
        "rimagent_pawn_move" or "rimagent_pawn_attack" => OrderPawn(tool, a),
        "rimagent_set_forbidden" => SetForbidden(a),
        "rimagent_camera_set" => Camera(a),
        "rimagent_step" => Step(a),
        _ => throw new BridgeException("unknown_tool", tool)
    };
    private static T OnMap<T>(Map map, Func<T> action)
    {
        var previous = Current.Game.CurrentMap;
        try { Current.Game.CurrentMap = map; return action(); }
        finally { Current.Game.CurrentMap = previous; }
    }
    private static object Build(JsonObject a, bool place)
    {
        var map = Map(a.Number("mapId", 0, int.MaxValue)); var cell = Cell(map, a);
        var name = a.Text("defName", 128);
        BuildableDef def = (BuildableDef)DefDatabase<ThingDef>.GetNamedSilentFail(name) ?? DefDatabase<TerrainDef>.GetNamedSilentFail(name);
        if (def?.blueprintDef == null || def.designationCategory == null) throw new BridgeException("not_buildable", "Def has no player blueprint.");
        var rotation = new Rot4(a.Number("rotation", 0, 3, 0));
        ThingDef stuff = null;
        if (def is ThingDef td && td.MadeFromStuff)
        {
            stuff = Def<ThingDef>(a.Text("stuff", 128));
            if (!GenStuff.AllowedStuffsFor(td).Contains(stuff)) throw new BridgeException("invalid_stuff", "Material is not allowed for this building.");
        }
        else if (a["stuff"] != null) throw new BridgeException("invalid_stuff", "This blueprint does not accept stuff.");
        return OnMap(map, () => BuildOnMap(def, stuff, rotation, cell, map, place));
    }
    private sealed class AgentBuildDesignator : Designator_Build
    {
        public AgentBuildDesignator(BuildableDef def, ThingDef stuff, Rot4 rotation) : base(def)
        {
            SetStuffDef(stuff);
            placingRot = rotation;
        }
    }
    private static object BuildOnMap(BuildableDef def, ThingDef stuff, Rot4 rotation, IntVec3 cell, Map map, bool place)
    {
        // Agent commands always use normal player rules, even if the player enabled god mode.
        var previousGodMode = DebugSettings.godMode;
        try
        {
            DebugSettings.godMode = false;
            var designator = new AgentBuildDesignator(def, stuff, rotation);
            string rejected = null;
            if (cell.Fogged(map)) rejected = "Cell is fogged.";
            else if (def.researchPrerequisites?.Any(r => !r.IsFinished) == true) rejected = "Required research is unfinished.";
            else if (BuildCopyCommandUtility.FindAllowedDesignator(def) == null) rejected = "Building is not available in the normal architect menu.";
            else
            {
                var report = designator.CanDesignateCell(cell);
                if (!report.Accepted) rejected = Rejection(report, "Placement rejected.");
            }
            if (!place) return new { canPlace = rejected == null, reason = rejected, defName = def.defName, x = cell.x, z = cell.z, rotation = rotation.AsInt };
            if (rejected != null) throw new BridgeException("placement_rejected", rejected);
            designator.DesignateSingleCell(cell);
            var blueprint = cell.GetThingList(map).FirstOrDefault(t => t is Blueprint && t.def.entityDefToBuild == def);
            var placed = cell.GetThingList(map).FirstOrDefault(t => t.def == def);
            bool terrainPlaced = def is TerrainDef terrain && (cell.GetTerrain(map) == terrain || map.terrainGrid.FoundationAt(cell) == terrain);
            if (blueprint == null && placed == null && !terrainPlaced) throw new BridgeException("placement_not_applied", "The native command did not place a blueprint or building; tutorial rules may have blocked it.");
            return new { accepted = true, blueprint = blueprint == null ? null : ThingView(blueprint), thing = placed == null ? null : ThingView(placed), defName = def.defName,
                completion = blueprint != null ? "Blueprint only; colonists must supply materials and perform construction." : "Placed immediately by the native architect command." };
        }
        finally { DebugSettings.godMode = previousGodMode; }
    }
    private static string Rejection(AcceptanceReport report, string fallback) => string.IsNullOrWhiteSpace(report.Reason) ? fallback : report.Reason;
    private static object CancelBuild(JsonObject a)
    {
        var thing = Thing(a);
        if (thing.Faction != Faction.OfPlayer || thing is not (Blueprint or Frame)) throw new BridgeException("not_blueprint", "Select a player blueprint or frame.");
        var id = thing.GetUniqueLoadID();
        OnMap(thing.Map, () => { var command = new Designator_Cancel(); var report = command.CanDesignateThing(thing);
            if (!report.Accepted) throw new BridgeException("designation_rejected", Rejection(report, "Cannot cancel this construction."));
            command.DesignateThing(thing); return true; });
        return new { removed = thing.Destroyed, thingId = id };
    }
    private static object Designate(JsonObject a)
    {
        var thing = Thing(a); var op = a.Text("operation", 32); var map = thing.Map;
        Designator designator = op switch {
            "mine" => new Designator_Mine(), "cut" => thing is Plant p && p.def.plant.IsTree && p.HarvestableNow ? new Designator_PlantsHarvestWood() : new Designator_PlantsCut(), "harvest" => new Designator_PlantsHarvest(),
            "haul" => new Designator_Haul(), "deconstruct" => new Designator_Deconstruct(), "hunt" => new Designator_Hunt(),
            "tame" => new Designator_Tame(), "cancel" => new Designator_Cancel(), _ => throw new BridgeException("invalid_arguments", "Unknown designation operation.")
        };
        return OnMap(map, () => {
            var report = designator.CanDesignateThing(thing);
            if (!report.Accepted) throw new BridgeException("designation_rejected", Rejection(report, $"Normal game rules reject {op} for {thing.def.defName}. It may already be designated or be ineligible."));
            designator.DesignateThing(thing);
            if (thing.Destroyed) return (object)new { thingId = thing.GetUniqueLoadID(), removed = true, designations = Array.Empty<string>() };
            return ThingView(thing);
        });
    }
    private static Zone Zone(JsonObject a)
    {
        var id = a.Text("zoneId", 128);
        return Map(a.Number("mapId", 0, int.MaxValue)).zoneManager.AllZones.FirstOrDefault(z => z.GetUniqueLoadID() == id)
            ?? throw new BridgeException("zone_not_found", "Zone not found on this map.");
    }
    private static ThingDef[] Allowed(JsonObject a)
    {
        if (a["allowedDefs"] == null) return null;
        if (a["allowedDefs"] is not JsonArray list || list.Count > 256) throw new BridgeException("invalid_arguments", "allowedDefs must be an array of at most 256 defNames.");
        return list.Select(n => Def<ThingDef>(n?.GetValue<string>() ?? "")).Distinct().ToArray();
    }
    private static ThingDef Plant(JsonObject a)
    {
        if (a["plantDef"] == null) return null;
        var def = Def<ThingDef>(a.Text("plantDef", 128));
        if (def.plant?.Sowable != true || def.plant.sowResearchPrerequisites?.Any(r => !r.IsFinished) == true)
            throw new BridgeException("plant_unavailable", "Plant is not sowable or its research is unfinished.");
        return def;
    }
    private static object CreateZone(JsonObject a)
    {
        var map = Map(a.Number("mapId", 0, int.MaxValue)); var cells = Rect(map, a, 256).Cells.ToArray();
        var kind = a.Text("kind", 32); var plant = Plant(a); var allowed = Allowed(a); int priority = a.Number("priority", 0, 5, 2);
        if (kind is not ("stockpile" or "growing")) throw new BridgeException("invalid_arguments", "kind must be stockpile or growing.");
        if (kind == "growing" && allowed != null || kind == "stockpile" && plant != null) throw new BridgeException("invalid_arguments", "Settings do not match zone kind.");
        foreach (var c in cells)
        {
            if (c.Fogged(map) || map.zoneManager.ZoneAt(c) != null || !c.Walkable(map)) throw new BridgeException("zone_cell_rejected", $"Cell {c.x},{c.z} is fogged, occupied by a zone or unwalkable.");
            var report = OnMap(map, () => (kind == "growing" ? (Designator)new Designator_ZoneAdd_Growing() : new Designator_ZoneAddStockpile_Resources()).CanDesignateCell(c));
            if (!report.Accepted) throw new BridgeException("zone_cell_rejected", $"Cell {c.x},{c.z}: {Rejection(report, "Native zone rules reject this cell.")}");
            if (kind == "growing" && c.GetFertility(map) < (plant?.plant.fertilityMin ?? 0.5f))
                throw new BridgeException("zone_cell_rejected", $"Cell {c.x},{c.z} cannot grow the selected crop.");
        }
        Zone zone = kind == "growing" ? new Zone_Growing(map.zoneManager) : new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
        map.zoneManager.RegisterZone(zone);
        foreach (var c in cells) zone.AddCell(c);
        if (zone is Zone_Growing grow && plant != null) grow.SetPlantDefToGrow(plant);
        if (zone is Zone_Stockpile stock) { stock.GetStoreSettings().Priority = (StoragePriority)priority; ApplyAllowed(stock.GetStoreSettings().filter, allowed); stock.slotGroup.RemoveHaulDesignationOnStoredThings(); }
        return ZoneView(zone);
    }
    private static object UpdateZone(JsonObject a)
    {
        var z = Zone(a); var plant = Plant(a); var allowed = Allowed(a); int priority = a.Number("priority", 0, 5, 2);
        bool? sow = a["allowSow"] == null ? null : a.Flag("allowSow"), cut = a["allowCut"] == null ? null : a.Flag("allowCut");
        if (z is Zone_Growing grow)
        {
            if (allowed != null || a["priority"] != null) throw new BridgeException("invalid_arguments", "Growing zones do not have storage filters.");
            if (plant != null && grow.Cells.Any(c => c.GetFertility(grow.Map) < plant.plant.fertilityMin)) throw new BridgeException("plant_unavailable", "Some zone cells lack crop fertility.");
            if (plant != null) grow.SetPlantDefToGrow(plant);
            if (sow.HasValue) grow.allowSow = sow.Value;
            if (cut.HasValue) grow.allowCut = cut.Value;
        }
        else if (z is Zone_Stockpile stock)
        {
            if (plant != null || sow.HasValue || cut.HasValue) throw new BridgeException("invalid_arguments", "Stockpiles do not have crop settings.");
            if (a["priority"] != null) stock.GetStoreSettings().Priority = (StoragePriority)priority;
            ApplyAllowed(stock.GetStoreSettings().filter, allowed);
        }
        else throw new BridgeException("unsupported_zone", "Unsupported zone kind.");
        return ZoneView(z);
    }
    private static void ApplyAllowed(ThingFilter filter, ThingDef[] defs)
    {
        if (defs == null) return;
        filter.SetDisallowAll(); foreach (var def in defs) filter.SetAllow(def, true);
    }
    private static object DeleteZone(JsonObject a) { var zone = Zone(a); var id = zone.GetUniqueLoadID(); zone.Delete(); return new { removed = true, zoneId = id }; }
    private static object EditBill(string tool, JsonObject a)
    {
        var bench = Bench(a); var stack = bench.BillStack;
        bool creating = tool == "rimagent_bill_create";
        Bill_Production bill;
        if (creating)
        {
            if (stack.Bills.Count >= 100) throw new BridgeException("bill_capacity", "At most 100 bills per workbench.");
            var recipe = Def<RecipeDef>(a.Text("recipeDef", 128));
            if (!bench.def.AllRecipes.Contains(recipe) || !recipe.AvailableNow) throw new BridgeException("recipe_unavailable", "Recipe is not currently available at this workbench.");
            bill = recipe.MakeNewBill() as Bill_Production ?? throw new BridgeException("not_production", "Only production bills are supported.");
        }
        else
        {
            var id = a.Text("billId", 128);
            bill = stack.Bills.OfType<Bill_Production>().FirstOrDefault(b => b.GetUniqueLoadID() == id) ?? throw new BridgeException("bill_not_found", "Production bill not found at this workbench.");
        }
        if (tool == "rimagent_bill_delete") { var id = bill.GetUniqueLoadID(); stack.Delete(bill); return new { removed = true, billId = id }; }
        var mode = a["repeatMode"] == null ? null : a.Text("repeatMode", 32);
        if (mode != null && mode is not ("count" or "target" or "forever")) throw new BridgeException("invalid_arguments", "repeatMode must be count, target or forever.");
        if (mode == "target" && !bill.recipe.WorkerCounter.CanCountProducts(bill)) throw new BridgeException("invalid_repeat_mode", "This recipe does not support a target product count.");
        int count = a.Number("count", 1, 9999, bill.repeatCount), target = a.Number("targetCount", 1, 9999, bill.targetCount);
        int radius = a.Number("ingredientRadius", 1, 999, (int)bill.ingredientSearchRadius);
        int index = a.Number("index", 0, Math.Max(0, stack.Bills.Count - (creating ? 0 : 1)), creating ? stack.Bills.Count : stack.Bills.IndexOf(bill));
        bool suspended = a["suspended"] == null ? bill.suspended : a.Flag("suspended");
        var allowed = Allowed(a);
        if (allowed != null && allowed.Any(d => !bill.recipe.fixedIngredientFilter.Allows(d))) throw new BridgeException("invalid_ingredient", "An allowed def is outside the recipe's ingredient filter.");
        var store = a["storeMode"] == null ? null : a.Text("storeMode", 32);
        if (store != null && store is not ("stockpile" or "drop")) throw new BridgeException("invalid_arguments", "storeMode must be stockpile or drop.");
        if (mode != null) bill.repeatMode = mode == "count" ? BillRepeatModeDefOf.RepeatCount : mode == "target" ? BillRepeatModeDefOf.TargetCount : BillRepeatModeDefOf.Forever;
        bill.repeatCount = count; bill.targetCount = target; bill.suspended = suspended; bill.ingredientSearchRadius = radius;
        ApplyAllowed(bill.ingredientFilter, allowed);
        if (store != null) bill.SetStoreMode(store == "drop" ? BillStoreModeDefOf.DropOnFloor : BillStoreModeDefOf.BestStockpile);
        if (creating) stack.AddBill(bill);
        if (a["index"] != null) stack.Reorder(bill, index - stack.Bills.IndexOf(bill));
        return BillView(bill);
    }
    private static object Research(string tool, JsonObject a)
    {
        var def = Def<ResearchProjectDef>(a.Text("researchDef", 128));
        if (tool == "rimagent_research_stop") Find.ResearchManager.StopProject(def);
        else
        {
            if (def.IsFinished || !def.CanStartNow) throw new BridgeException("research_unavailable", "Research is completed or normal prerequisites/bench conditions are unmet.");
            Find.ResearchManager.SetCurrentProject(def);
        }
        return ResearchView(def);
    }
    private static object SetWork(JsonObject a)
    {
        var pawn = Pawn(a); var work = Def<WorkTypeDef>(a.Text("workDef", 128)); int priority = a.Number("priority", 0, 4);
        if (pawn.workSettings == null || pawn.WorkTypeIsDisabled(work)) throw new BridgeException("work_disabled", "This pawn cannot perform the work type.");
        Current.Game.playSettings.useWorkPriorities = true; pawn.workSettings.SetPriority(work, priority); return WorkView(pawn);
    }
    private static object OrderPawn(string tool, JsonObject a)
    {
        var pawn = Pawn(a);
        if (!pawn.Drafted || pawn.Downed || pawn.Dead || pawn.InMentalState) throw new BridgeException("pawn_unavailable", "Pawn must be drafted and able to take orders.");
        Job job;
        if (tool == "rimagent_pawn_move")
        {
            var cell = Cell(pawn.Map, a);
            if (cell.Fogged(pawn.Map) || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) throw new BridgeException("unreachable", "Destination is fogged or unreachable.");
            job = JobMaker.MakeJob(JobDefOf.Goto, cell);
        }
        else
        {
            var target = Thing(a); var mode = a.Text("mode", 16);
            if (!target.HostileTo(pawn)) throw new BridgeException("not_hostile", "Target is not hostile to this colonist.");
            if (mode == "melee")
            {
                if (!pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly)) throw new BridgeException("unreachable", "Melee target is unreachable.");
                job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            }
            else if (mode == "ranged")
            {
                if (pawn.TryGetAttackVerb(target)?.CanHitTarget(target) != true) throw new BridgeException("cannot_hit", "No usable attack verb can hit this target from the current position.");
                job = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
            }
            else throw new BridgeException("invalid_arguments", "mode must be melee or ranged.");
        }
        job.playerForced = true; pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        return new { accepted = pawn.CurJob == job, pawnId = pawn.GetUniqueLoadID(), job = pawn.CurJob?.def.defName, x = pawn.Position.x, z = pawn.Position.z };
    }
    private static object SetForbidden(JsonObject a)
    {
        var t = Thing(a); bool forbidden = a.Flag("forbidden");
        if (t.def.category != ThingCategory.Item && t.Faction != Faction.OfPlayer) throw new BridgeException("not_owned", "Only items or player things can be forbidden.");
        if (t.TryGetComp<CompForbiddable>() == null) throw new BridgeException("not_forbiddable", "Thing cannot be forbidden.");
        t.SetForbidden(forbidden, false); return ThingView(t);
    }
    private static object Camera(JsonObject a)
    {
        var map = Map(a.Number("mapId", 0, int.MaxValue)); var cell = Cell(map, a); int zoom = a.Number("zoom", 11, 60, 24);
        Current.Game.CurrentMap = map; Find.CameraDriver.JumpToCurrentMapLoc(cell); Find.CameraDriver.SetRootSize(zoom);
        return new { mapId = map.uniqueID, x = cell.x, z = cell.z, zoom };
    }
    private object Step(JsonObject a)
    {
        if (Find.WindowStack.Windows.Any(w => w.forcePause)) throw new BridgeException("simulation_blocked", "A game window forces pause. Inspect dialogs_list and resolve its native options before advancing.");
        if (stepUntil.HasValue) throw new BridgeException("step_running", "A simulation step is already running. Pause or wait for its target tick.");
        int ticks = a.Number("ticks", 1, 60000), speed = a.Number("speed", 1, 3, 1);
        stepOwner = Current.Game; stepUntil = checked(Find.TickManager.TicksGame + ticks);
        stepRaidCount = Find.StoryWatcher.statsRecord.numRaidsEnemy; stepStopOnThreat = a["stopOnThreat"] == null || a.Flag("stopOnThreat"); stepStopReason = null;
        stepHostiles.Clear(); foreach (var p in Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).Where(p => p.HostileTo(Faction.OfPlayer))) stepHostiles.Add(p.thingIDNumber);
        Find.TickManager.CurTimeSpeed = (TimeSpeed)speed;
        return new { accepted = true, startTick = Find.TickManager.TicksGame, targetTick = stepUntil };
    }
    public void UpdateOperations()
    {
        UpdateVisualWatch();
        ExpireGizmoInteraction();
        if (!stepUntil.HasValue) return;
        if (!ReferenceEquals(Current.Game, stepOwner)) { stepUntil = null; stepOwner = null; return; }
        if (stepStopOnThreat && (Find.StoryWatcher.statsRecord.numRaidsEnemy > stepRaidCount || Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).Any(p => p.HostileTo(Faction.OfPlayer) && !stepHostiles.Contains(p.thingIDNumber))))
        { stepStopReason = "new_hostile_or_raid"; ReleaseOperations(); return; }
        if (Find.TickManager.TicksGame >= stepUntil.Value || Find.TickManager.Paused) { stepStopReason = Find.TickManager.Paused ? "game_paused" : "target_reached"; ReleaseOperations(); }
    }
    public void ReleaseOperations()
    {
        ReleaseGizmoInteraction();
        if (stepUntil.HasValue && ReferenceEquals(Current.Game, stepOwner)) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        stepUntil = null; stepOwner = null;
    }
}
