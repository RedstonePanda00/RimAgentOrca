using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using RimAgent.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimAgent;

internal sealed partial class OrcaAgentGameAdapter
{
    private sealed record GizmoObservation(Thing Owner, Map Map, string[] Signatures, DateTime Expires);
    private sealed record GizmoTargetObservation(string Id, Thing Owner, Map Map, ITargetingSource Source,
        Action<LocalTargetInfo> Action, TargetingParameters Parameters, DateTime Expires);
    private readonly Dictionary<string, GizmoObservation> gizmoObservations = new();
    private GizmoTargetObservation gizmoTarget;

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "action")]
    private static extern ref Action<LocalTargetInfo> TargetAction(Targeter targeter);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "targetParams")]
    private static extern ref TargetingParameters TargetParameters(Targeter targeter);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "targetValidator")]
    private static extern ref Func<LocalTargetInfo, bool> TargetValidator(Targeter targeter);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "needsStopTargetingCall")]
    private static extern ref bool TargetNeedsStop(Targeter targeter);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ConfirmStillValid")]
    private static extern void ValidateTargeter(Targeter targeter);

    private static Thing GizmoOwner(JsonObject a)
    {
        var thing = Thing(a);
        if (thing.Faction != Faction.OfPlayer || !thing.def.selectable)
            throw new BridgeException("not_player_thing", "Select a visible, selectable player-owned thing or pawn.");
        return thing;
    }
    private static T NormalGizmoRules<T>(Func<T> action)
    {
        bool god = DebugSettings.godMode;
        try { DebugSettings.godMode = false; return action(); }
        finally { DebugSettings.godMode = god; }
    }
    private static bool IsDevGizmo(Gizmo gizmo) => gizmo is Command_Ability { devGizmo: true }
        || (gizmo is Command command && (command.Label ?? "").StartsWith("DEV:", StringComparison.OrdinalIgnoreCase));
    private static List<Gizmo> NativeGizmos(Thing owner) => owner.GetGizmos().Where(g => g != null && g.Visible && !IsDevGizmo(g))
        .OrderBy(g => g.Order).Take(129).ToList();
    private static string GizmoSignature(Gizmo g) => MiniJson.Serialize(new[] {
        g.GetType().AssemblyQualifiedName, (g as Command)?.Label ?? "", (g as Command)?.Desc ?? "",
        (g as Command)?.groupKey.ToString() ?? "", (g as Command)?.groupKeyIgnoreContent.ToString() ?? "" });
    private static string GizmoLabel(Gizmo gizmo) => string.IsNullOrEmpty((gizmo as Command)?.Label) ? gizmo.GetType().Name : ((Command)gizmo).Label;

    private object Gizmos(JsonObject a)
    {
        var owner = GizmoOwner(a);
        foreach (var key in gizmoObservations.Where(p => p.Value.Expires < DateTime.UtcNow).Select(p => p.Key).ToArray()) gizmoObservations.Remove(key);
        if (gizmoObservations.Count >= 128) gizmoObservations.Remove(gizmoObservations.Keys.First());
        var selected = Find.Selector.SelectedObjects.ToArray();
        return OnMap(owner.Map, () => NormalGizmoRules(() => {
            try
            {
                // Gizmo enumeration often consults selection. Restore it before returning the read.
                Find.Selector.SelectedObjects.Clear(); Find.Selector.SelectedObjects.Add(owner);
                var all = NativeGizmos(owner); var gizmos = all.Take(128).ToArray();
                var signatures = gizmos.Select(GizmoSignature).ToArray(); var id = Guid.NewGuid().ToString("N");
                var duplicates = signatures.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
                gizmoObservations[id] = new(owner, owner.Map, signatures, DateTime.UtcNow.AddSeconds(60));
                return new { observationId = id, thingId = owner.GetUniqueLoadID(), mapId = owner.Map.uniqueID, truncated = all.Count > 128,
                    options = gizmos.Select((g, i) => {
                        var command = g as Command;
                        bool disabled = g.Disabled;
                        bool ambiguous = duplicates.Contains(signatures[i]);
                        return new { optionIndex = i, text = GizmoLabel(g), description = command?.Desc,
                            type = g.GetType().FullName, disabled, reason = g.disabledReason, supported = command != null && !ambiguous,
                            unsupportedReason = command == null ? "Custom non-command widget needs its own input adapter." : ambiguous ? "Identical commands cannot be safely distinguished." : null,
                            active = g is Command_Toggle toggle ? (bool?)toggle.isActive() : null,
                            interaction = g is Command_Target or Command_VerbTarget || g is Command_Ability { Ability.def.targetRequired: true } ? "target" : g is Command_Toggle ? "toggle" : command != null ? "command" : "custom_widget" };
                    }).ToArray() };
            }
            finally { Find.Selector.SelectedObjects.Clear(); Find.Selector.SelectedObjects.AddRange(selected); }
        }));
    }

    private object InvokeGizmo(JsonObject a)
    {
        var owner = GizmoOwner(a); string id = a.Text("observationId", 64), expected = a.Text("expectedText", 4096);
        string button = a["button"] == null ? "left" : a.Text("button", 8);
        if (button is not ("left" or "right")) throw new BridgeException("invalid_arguments", "button must be left or right.");
        if (!gizmoObservations.TryGetValue(id, out var seen) || seen.Expires < DateTime.UtcNow || seen.Owner != owner || seen.Map != owner.Map)
            throw new BridgeException("gizmo_stale", "Read gizmos_list again for this thing.");
        int index = a.Number("optionIndex", 0, Math.Max(0, seen.Signatures.Length - 1));
        if (Find.Targeter.IsTargeting || Find.WorldTargeter.IsTargeting || Find.WindowStack.Windows.Any(w => w.forcePause || w is FloatMenu))
            throw new BridgeException("interaction_busy", "Resolve the current target or window before invoking another Gizmo.");
        Current.Game.CurrentMap = owner.Map;
        Find.Selector.ClearSelection(); Find.Selector.Select(owner, false, false);
        return NormalGizmoRules(() => {
            var gizmos = NativeGizmos(owner).Take(128).ToArray();
            var signatures = gizmos.Select(GizmoSignature).ToArray();
            if (!signatures.SequenceEqual(seen.Signatures) || index >= gizmos.Length || GizmoLabel(gizmos[index]) != expected)
                throw new BridgeException("gizmo_stale", "Native commands changed; read gizmos_list again.");
            if (gizmos[index] is not Command command) throw new BridgeException("gizmo_requires_ui", "This custom widget has no native command input adapter.");
            if (signatures.Count(s => s == signatures[index]) != 1) throw new BridgeException("gizmo_ambiguous", "Native commands are indistinguishable.");
            if (command.Disabled) throw new BridgeException("gizmo_disabled", command.disabledReason ?? "The game disabled this command.");
            if (!TutorSystem.AllowAction(command.TutorTagSelect)) throw new BridgeException("gizmo_disabled", "The native tutorial blocks this command.");
            // GUI-only ability checks must also hold before calling its native ProcessInput.
            if (command is Command_Ability ability && (!(bool)ability.Ability.CanCast || (ability.Ability.Casting && !ability.Ability.CanQueueCast)))
                throw new BridgeException("gizmo_disabled", "The native ability cannot currently cast or queue.");
            gizmoObservations.Remove(id);
            var previousEvent = Event.current;
            try
            {
                var ev = new Event { type = EventType.MouseDown, button = button == "right" ? 1 : 0 };
                Event.current = ev;
                var options = button == "right" ? command.RightClickFloatMenuOptions.ToList() : new List<FloatMenuOption>();
                if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options) { vanishIfMouseDistant = false });
                else
                {
                    TutorSystem.Notify_Event(command.TutorTagSelect);
                    command.ProcessInput(ev);
                    command.ProcessGroupInput(ev, new List<Gizmo> { command });
                }
            }
            finally { Event.current = previousEvent; }
            return GizmoInteractionResult(owner);
        });
    }

    private object GizmoInteractionResult(Thing owner)
    {
        var targeter = Find.Targeter;
        gizmoTarget = targeter.IsTargeting ? new(Guid.NewGuid().ToString("N"), owner, owner.Map, targeter.targetingSource,
            TargetAction(targeter), TargetParameters(targeter), DateTime.UtcNow.AddSeconds(60)) : null;
        return new { accepted = true, thingId = owner.GetUniqueLoadID(), interactionId = gizmoTarget?.Id,
            next = targeter.IsTargeting ? "map_target" : Find.WorldTargeter.IsTargeting ? "world_target_requires_ui"
                : Find.DesignatorManager.SelectedDesignator != null ? "designator_requires_ui" : Find.WindowStack.Windows.Any(w => w.forcePause || w is FloatMenu) ? "window" : "observe_result",
            designator = Find.DesignatorManager.SelectedDesignator?.GetType().FullName,
            windows = Dialogs(), otherWindows = Find.WindowStack.Windows.Where(w => !w.forcePause && w is not FloatMenu)
                .Select(w => new { type = w.GetType().Name, dialogId = WindowId(w), canClose = w.closeOnCancel || w.doCloseButton || w.doCloseX }).ToArray(),
            completion = "Native command invoked; inspect state/jobs to verify effects. Custom widgets and world targeting may require UI." };
    }
    private bool OwnsNativeTarget()
    {
        var t = Find.Targeter; var seen = gizmoTarget;
        return seen != null && t.IsTargeting && !seen.Owner.Destroyed && seen.Owner.Map == seen.Map && Current.Game.CurrentMap == seen.Map
            && Find.Selector.NumSelected == 1 && Find.Selector.SingleSelectedThing == seen.Owner
            && ReferenceEquals(t.targetingSource, seen.Source) && ReferenceEquals(TargetAction(t), seen.Action) && ReferenceEquals(TargetParameters(t), seen.Parameters);
    }
    private object TargetGizmo(JsonObject a)
    {
        var owner = GizmoOwner(a);
        if (gizmoTarget == null || gizmoTarget.Id != a.Text("interactionId", 64) || gizmoTarget.Owner != owner || gizmoTarget.Expires < DateTime.UtcNow || !OwnsNativeTarget())
            throw new BridgeException("target_stale", "The pending native target changed, expired or was cancelled.");
        if (a["cancel"] != null && a.Flag("cancel")) { ReleaseGizmoInteraction(); return new { cancelled = true }; }
        bool thingTarget = a["targetThingId"] != null;
        if (thingTarget == (a["x"] != null || a["z"] != null)) throw new BridgeException("invalid_arguments", "Supply targetThingId OR both x and z.");
        LocalTargetInfo target;
        if (thingTarget)
        {
            var targetArgs = (JsonObject)a.DeepClone(); targetArgs["thingId"] = a.Text("targetThingId", 128);
            target = new LocalTargetInfo(Thing(targetArgs));
        }
        else { var cell = Cell(owner.Map, a); if (cell.Fogged(owner.Map)) throw new BridgeException("fogged", "Target cell is fogged."); target = new LocalTargetInfo(cell); }
        return NormalGizmoRules(() => {
            var t = Find.Targeter; ValidateTargeter(t);
            if (!OwnsNativeTarget()) throw new BridgeException("target_stale", "Native targeting is no longer valid.");
            var source = t.targetingSource; var parameters = source?.targetParams ?? TargetParameters(t);
            if (parameters == null || !parameters.CanTarget(target.ToTargetInfo(owner.Map), source)
                || (source != null && target.Thing == source.Caster && !parameters.canTargetSelf))
                throw new BridgeException("target_rejected", "Native targeting parameters reject this target.");
            if (source != null && (!source.ValidateTarget(target) || (!(target.Thing is Pawn) && !source.IsMeleeAttack && !source.CanHitTarget(target))))
                throw new BridgeException("target_rejected", "Native ability/verb validation or range rejects this target.");
            if (TargetValidator(t) != null && !TargetValidator(t)(target)) throw new BridgeException("target_rejected", "Native target validator rejected this target.");
            if (source != null && ((!source.CasterIsPawn && source.Caster is not Building_Turret) || t.targetingSourceAdditionalPawns?.Count > 0 || t.targetingSourceGetter != null))
                throw new BridgeException("target_requires_ui", "This native target uses grouped/non-pawn selection; it is not supported by the single-owner adapter.");
            TargetNeedsStop(t) = true;
            // The game owns casting, cooldowns, costs, validation and any resulting jobs.
            if (source != null)
            {
                if (source.Caster is Building_Turret turret) turret.OrderAttack(target);
                else source.OrderForceTarget(target);
                if (source.DestinationSelector != null) t.BeginTargeting(source.DestinationSelector, source);
            }
            else if (TargetAction(t) != null) TargetAction(t)(target);
            else throw new BridgeException("target_requires_ui", "Native target has no supported action.");
            if (TargetNeedsStop(t)) t.StopTargeting();
            return GizmoInteractionResult(owner);
        });
    }
    private void ExpireGizmoInteraction()
    {
        if (gizmoTarget != null && gizmoTarget.Expires < DateTime.UtcNow) ReleaseGizmoInteraction();
    }
    private void ReleaseGizmoInteraction()
    {
        if (gizmoTarget != null && OwnsNativeTarget()) Find.Targeter.StopTargeting();
        gizmoTarget = null; gizmoObservations.Clear();
    }
}
