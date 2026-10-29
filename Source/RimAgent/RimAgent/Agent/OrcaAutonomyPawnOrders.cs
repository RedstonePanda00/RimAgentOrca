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
    private sealed record OrderObservation(Pawn Pawn, Map Map, IntVec3 Cell, string[] Labels, DateTime Expires);
    private readonly Dictionary<string, OrderObservation> orderObservations = new();
    private static List<FloatMenuOption> NativeOrders(Pawn pawn, IntVec3 cell) => OnMap(pawn.Map, () => {
        var previous = FloatMenuMakerMap.makingFor;
        try { return FloatMenuMakerMap.GetOptions([pawn], cell.ToVector3Shifted(), out _); }
        finally { FloatMenuMakerMap.makingFor = previous; }
    });
    private object PawnOrders(JsonObject a)
    {
        var pawn = Pawn(a); var cell = Cell(pawn.Map, a);
        if (cell.Fogged(pawn.Map)) throw new BridgeException("fogged", "Only visible cells can be ordered.");
        foreach (var id in orderObservations.Where(p => p.Value.Expires < DateTime.UtcNow).Select(p => p.Key).ToArray()) orderObservations.Remove(id);
        if (orderObservations.Count >= 128) orderObservations.Remove(orderObservations.Keys.First());
        var options = NativeOrders(pawn, cell); var token = Guid.NewGuid().ToString("N");
        orderObservations[token] = new(pawn, pawn.Map, cell, options.Select(o => o.Label).ToArray(), DateTime.UtcNow.AddSeconds(60));
        return new { orderId = token, pawnId = pawn.GetUniqueLoadID(), x = cell.x, z = cell.z,
            options = options.Select((o,i) => new { optionIndex = i, text = o.Label, disabled = o.Disabled }).ToArray() };
    }
    private object ChoosePawnOrder(JsonObject a)
    {
        var id = a.Text("orderId", 64); var pawn = Pawn(a); var expected = a.Text("expectedText", 4096);
        if (!orderObservations.TryGetValue(id, out var seen) || seen.Expires < DateTime.UtcNow || seen.Pawn != pawn || seen.Map != pawn.Map)
            throw new BridgeException("order_stale", "Read pawn_orders again for this pawn and cell.");
        int index = a.Number("optionIndex", 0, Math.Max(0, seen.Labels.Length - 1));
        if (index >= seen.Labels.Length || seen.Labels[index] != expected) throw new BridgeException("order_stale", "Observed option does not match.");
        var options = NativeOrders(pawn, seen.Cell);
        if (index >= options.Count || options[index].Label != expected) throw new BridgeException("order_stale", "Native options changed; read them again.");
        if (options[index].Disabled) throw new BridgeException("order_disabled", options[index].Label);
        OnMap(pawn.Map, () => { options[index].Chosen(true, null); return true; });
        orderObservations.Remove(id);
        return new { accepted = true, pawnId = pawn.GetUniqueLoadID(), job = pawn.CurJob?.def.defName, completion = "Native option invoked; query pawn/target state for completion." };
    }
    private static object PawnDetails(JsonObject a)
    {
        var map = Map(a.Number("mapId",0,int.MaxValue)); var id = a.Text("pawnId",128);
        var p = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.GetUniqueLoadID() == id && !p.Position.Fogged(map))
            ?? throw new BridgeException("pawn_not_found", "Visible pawn not found.");
        return new { pawnId = id, name = p.Name?.ToStringFull, label = p.LabelShort, x = p.Position.x, z = p.Position.z, playerOwned = p.Faction == Faction.OfPlayer,
            hostile = p.HostileTo(Faction.OfPlayer), dead = p.Dead, downed = p.Downed, drafted = p.Drafted, mentalState = p.MentalStateDef?.defName,
            job = p.CurJob?.def.defName, health = p.health.summaryHealth.SummaryHealthPercent, bleedRate = p.health.hediffSet.BleedRateTotal,
            canMove = p.health.capacities.CapableOf(PawnCapacityDefOf.Moving), canManipulate = p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation),
            needs = p.needs?.AllNeeds.Select(n => new { defName = n.def.defName, level = n.CurLevelPercentage }).ToArray(),
            hediffs = p.health.hediffSet.hediffs.Select(h => new { defName = h.def.defName, label = h.Label, severity = h.Severity, part = h.Part?.Label,
                bleeding = h.BleedRate, needsTend = h.TendableNow(), immunity = p.health.immunity.GetImmunity(h.def) }).ToArray(),
            medicalCare = p.playerSettings?.medCare.ToString(), selfTend = p.playerSettings?.selfTend,
            equipment = p.equipment?.AllEquipmentListForReading.Select(t => new { thingId = t.GetUniqueLoadID(), defName = t.def.defName, label = t.Label }).ToArray(),
            apparel = p.apparel?.WornApparel.Select(t => new { thingId = t.GetUniqueLoadID(), defName = t.def.defName, label = t.Label, hp = t.HitPoints }).ToArray(),
            inventory = p.inventory?.innerContainer.Select(t => new { thingId = t.GetUniqueLoadID(), defName = t.def.defName, count = t.stackCount }).ToArray() };
    }
    private static object MedicalCare(JsonObject a)
    {
        var p = Pawn(a); int care = a.Number("care",0,4);
        p.playerSettings.medCare = (MedicalCareCategory)care;
        if (a["selfTend"] != null) p.playerSettings.selfTend = a.Flag("selfTend");
        return new { pawnId = p.GetUniqueLoadID(), medicalCare = p.playerSettings.medCare.ToString(), selfTend = p.playerSettings.selfTend };
    }
}
