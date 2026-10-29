using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using RimAgent.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimAgent;

internal sealed partial class OrcaAgentGameAdapter
{
    private sealed class DialogObservation
    {
        public DiaNode Node;
        public string Id;
    }
    private readonly ConditionalWeakTable<Dialog_NodeTree, DialogObservation> dialogObservations = new();
    private sealed class WindowObservation { public string Id = Guid.NewGuid().ToString("N"); }
    private readonly ConditionalWeakTable<Window, WindowObservation> windowObservations = new();
    private string WindowId(Window w) => windowObservations.GetValue(w,_ => new WindowObservation()).Id;
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_InteractionDelayExpired")]
    private static extern bool MessageInteractive(Dialog_MessageBox dialog);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "curName")]
    private static extern ref string NameValue(Dialog_GiveName dialog);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "curSecondName")]
    private static extern ref string SecondNameValue(Dialog_GiveName dialog);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "settlement")]
    private static extern ref Settlement NamedSettlement(Dialog_NamePlayerSettlement dialog);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "settlement")]
    private static extern ref Settlement NamedCombinedSettlement(Dialog_NamePlayerFactionAndSettlement dialog);

    // RimWorld 1.7 has no public non-GUI option activation entry point.
    // These accessors call the native implementation without duplicating dialog actions.
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "curNode")]
    private static extern ref DiaNode NativeNode(Dialog_NodeTree dialog);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "text")]
    private static extern ref string NativeOptionText(DiaOption option);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_InteractiveNow")]
    private static extern bool NativeInteractive(Dialog_NodeTree dialog);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Activate")]
    private static extern void NativeActivate(DiaOption option);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "options")]
    private static extern ref List<FloatMenuOption> NativeMenuOptions(FloatMenu menu);

    private object Dialogs() => new {
        windows = Find.WindowStack.Windows.Where(w => w.forcePause || w is FloatMenu).Select(w => {
            if (w is FloatMenu menu)
                return (object)new { type = w.GetType().Name, supported = true, forcePause = w.forcePause, dialogId = WindowId(w),
                    options = NativeMenuOptions(menu).Select((o, i) => new { optionIndex = i, text = o.Label, disabled = o.Disabled, supported = true }).ToArray() };
            if (w is Dialog_MessageBox box && w.GetType() == typeof(Dialog_MessageBox))
                return (object)new { type = w.GetType().Name, supported = true, forcePause = true, dialogId = WindowId(w), text = box.text.ToString(), title = box.title,
                    interactive = MessageInteractive(box), options = new[] { (box.buttonAText,box.buttonAAction), (box.buttonBText,box.buttonBAction), (box.buttonCText,box.buttonCAction) }
                        .Select((b,i) => new { optionIndex = i, text = b.Item1, disabled = i == 0 && !MessageInteractive(box), supported = true, dismissOnly = b.Item2 == null }).Where(b => b.text != null).ToArray() };
            if (w is Dialog_NamePlayerFactionAndSettlement)
                return new { type = w.GetType().Name, supported = true, forcePause = true, dialogId = WindowId(w), tool = "dialog_name", suggestedName = NameValue((Dialog_GiveName)w), suggestedSecondName = SecondNameValue((Dialog_GiveName)w), requiresSecondName = true };
            if (w is Dialog_NamePlayerFaction or Dialog_NamePlayerSettlement)
                return new { type = w.GetType().Name, supported = true, forcePause = true, dialogId = WindowId(w), tool = "dialog_name", suggestedName = NameValue((Dialog_GiveName)w) };
            if (w is Dialog_Trade)
                return new { type = w.GetType().Name, supported = true, forcePause = true, tool = "trade_get", mapId = TradeSession.playerNegotiator?.Map?.uniqueID };
            if (w.GetType() != typeof(Dialog_NodeTree) && w.GetType() != typeof(Dialog_NodeTreeWithFactionInfo))
                return new { type = w.GetType().Name, supported = false, forcePause = true, dialogId = WindowId(w), canClose = w.closeOnCancel || w.doCloseButton || w.doCloseX };
            var tree = (Dialog_NodeTree)w;
            var node = NativeNode(tree);
            var observation = dialogObservations.GetValue(tree, _ => new DialogObservation());
            if (!ReferenceEquals(observation.Node, node)) { observation.Node = node; observation.Id = Guid.NewGuid().ToString("N"); }
            return new { type = w.GetType().Name, supported = true, forcePause = true, dialogId = observation.Id,
                text = node.text.Resolve(), interactive = NativeInteractive(tree),
                options = node.options.Select((o, i) => new { optionIndex = i, text = NativeOptionText(o), disabled = o.disabled,
                    reason = o.disabledReason, supported = o.GetType() == typeof(DiaOption) && o.hyperlink.def == null,
                    dismissOnly = o.resolveTree && o.action == null && o.link == null && o.linkLateBind == null }).ToArray() };
        }).ToArray()
    };
    private object ChooseDialog(JsonObject a)
    {
        var id = a.Text("dialogId", 64); var expected = a.Text("expectedText", 4096);
        var menu = Find.WindowStack.Windows.OfType<FloatMenu>().FirstOrDefault(w => WindowId(w) == id);
        if (menu != null)
        {
            var options = NativeMenuOptions(menu);
            int menuIndex = a.Number("optionIndex", 0, Math.Max(0, options.Count - 1));
            if (menuIndex >= options.Count || options[menuIndex].Label != expected) throw new BridgeException("dialog_stale", "Native menu options changed.");
            var menuOption = options[menuIndex];
            if (menuOption.Disabled) throw new BridgeException("option_disabled", menuOption.Label);
            if (menuOption.tutorTag != null && !TutorSystem.AllowAction(menuOption.tutorTag)) throw new BridgeException("option_disabled", "The native tutorial blocks this option.");
            menuOption.Chosen(menu.givesColonistOrders, menu);
            if (menuOption.tutorTag != null) TutorSystem.Notify_Event(menuOption.tutorTag);
            Find.WindowStack.TryRemove(menu);
            return new { accepted = true, closed = !Find.WindowStack.Windows.Contains(menu), completion = "Native option chosen; query resulting state." };
        }
        var box = Find.WindowStack.Windows.OfType<Dialog_MessageBox>().FirstOrDefault(w => w.GetType() == typeof(Dialog_MessageBox) && WindowId(w) == id);
        if (box != null)
        {
            int i = a.Number("optionIndex",0,2);
            var labels = new[] { box.buttonAText, box.buttonBText, box.buttonCText };
            var actions = new[] { box.buttonAAction, box.buttonBAction, box.buttonCAction };
            if (labels[i] == null || labels[i] != expected) throw new BridgeException("dialog_stale", "Message option changed.");
            if (i == 0 && !MessageInteractive(box)) throw new BridgeException("dialog_not_interactive", "Native interaction delay has not expired.");
            actions[i]?.Invoke();
            if (i != 2 || box.buttonCClose) box.Close();
            return new { accepted = true, closed = !Find.WindowStack.Windows.Contains(box) };
        }
        var tree = Find.WindowStack.Windows.OfType<Dialog_NodeTree>().FirstOrDefault(w =>
            dialogObservations.TryGetValue(w, out var observed) && observed.Id == id && ReferenceEquals(observed.Node, NativeNode(w)))
            ?? throw new BridgeException("dialog_stale", "Dialog closed or changed. Read dialogs_list again.");
        var node = NativeNode(tree);
        int index = a.Number("optionIndex", 0, Math.Max(0, node.options.Count - 1));
        if (index >= node.options.Count) throw new BridgeException("dialog_stale", "No option is available.");
        var option = node.options[index];
        if (option.GetType() != typeof(DiaOption) || option.hyperlink.def != null) throw new BridgeException("unsupported_option", "This option needs its specialized game interface.");
        if (NativeOptionText(option) != expected) throw new BridgeException("dialog_stale", "Option text changed. Read dialogs_list again.");
        if (!NativeInteractive(tree)) throw new BridgeException("dialog_not_interactive", "The native interaction delay has not expired.");
        if (option.disabled) throw new BridgeException("option_disabled", string.IsNullOrWhiteSpace(option.disabledReason) ? "The game disabled this option." : option.disabledReason);
        NativeActivate(option);
        return new { accepted = true, dialogId = id, optionIndex = index, closed = !Find.WindowStack.Windows.Contains(tree) };
    }
    private object NameDialog(JsonObject a)
    {
        var id = a.Text("dialogId",64); var name = a.Text("name",64).Trim();
        var window = Find.WindowStack.Windows.FirstOrDefault(w => WindowId(w) == id);
        if (window is Dialog_NamePlayerFactionAndSettlement combined)
        {
            var second = a.Text("secondName",64).Trim();
            // Validate both fields before the first mutation, as the native dialog does.
            if (!NamePlayerFactionDialogUtility.IsValidName(name) || !NamePlayerSettlementDialogUtility.IsValidName(second))
                throw new BridgeException("invalid_name","Native faction or settlement name rules reject this name pair.");
            NamePlayerFactionDialogUtility.Named(name);
            NamePlayerSettlementDialogUtility.Named(NamedCombinedSettlement(combined),second);
        }
        else if (window is Dialog_NamePlayerFaction)
        {
            if (!NamePlayerFactionDialogUtility.IsValidName(name)) throw new BridgeException("invalid_name","Native faction-name rules reject this name.");
            NamePlayerFactionDialogUtility.Named(name);
        }
        else if (window is Dialog_NamePlayerSettlement settlement)
        {
            if (!NamePlayerSettlementDialogUtility.IsValidName(name)) throw new BridgeException("invalid_name","Native settlement-name rules reject this name.");
            NamePlayerSettlementDialogUtility.Named(NamedSettlement(settlement),name);
        }
        else throw new BridgeException("dialog_stale","The observed naming window is no longer available.");
        window.Close(); return new { accepted = true, name };
    }
    private object CloseDialog(JsonObject a)
    {
        var id = a.Text("dialogId",64); var type = a.Text("expectedType",128);
        var window = Find.WindowStack.Windows.FirstOrDefault(w => WindowId(w) == id && w.GetType().Name == type);
        if (window == null) throw new BridgeException("dialog_stale","Window no longer available.");
        if (window is Dialog_NodeTree or Dialog_MessageBox or Dialog_GiveName or Dialog_Trade || !(window.closeOnCancel || window.doCloseButton || window.doCloseX))
            throw new BridgeException("close_unavailable","Use the window's decision-specific tools; it has no generic close operation.");
        window.Close(); return new { closed = true };
    }
}
