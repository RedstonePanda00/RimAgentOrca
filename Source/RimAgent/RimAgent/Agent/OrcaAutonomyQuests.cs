using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using RimAgent.Core;
using RimWorld;
using Verse;

namespace RimAgent;

internal sealed partial class OrcaAgentGameAdapter
{
    private sealed record QuestObservation(string Id, Game Game, string Signature, DateTime Expires);
    private readonly ConditionalWeakTable<Quest, QuestObservation> questObservations = new();

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "AcceptQuestByInterface")]
    private static extern void NativeAcceptQuest(MainTabWindow_Quests window, Action preAcceptAction, bool requiresAccepter);

    private static Letter GameLetter(JsonObject a) => GetLatestLetterTool.KnownLetters().FirstOrDefault(l => l.GetUniqueLoadID() == a.Text("letterId", 128))
        ?? throw new BridgeException("letter_not_found", "Letter is no longer in the active stack or archive.");

    private static object LetterSummary(Letter l) => new {
        letterId = l.GetUniqueLoadID(), label = l.Label.Resolve(), type = l.GetType().FullName,
        defName = l.def?.defName, l.arrivalTick, archivedOnly = l.ArchivedOnly,
        questId = l is ChoiceLetter { quest: { hidden: false, hiddenInUI: false } q } ? (int?)q.id : null,
        faction = l.relatedFaction?.Name, expiresAtTick = l is LetterWithTimeout { TimeoutActive: true } t ? (int?)t.disappearAtTick : null
    };

    private static object Letters(JsonObject a)
    {
        string source = a["source"] == null ? "all" : a.Text("source", 16);
        if (source is not ("all" or "active" or "archive")) throw new BridgeException("invalid_arguments", "source must be all, active or archive.");
        return Page(GetLatestLetterTool.KnownLetters().Where(l => source == "all" || (source == "archive" ? l.ArchivedOnly : !l.ArchivedOnly))
            .OrderByDescending(l => l.arrivalTick).ThenByDescending(l => l.ID).Select(LetterSummary), a);
    }

    private static object TextPage(string text, JsonObject a)
    {
        text ??= "";
        int offset = a.Number("textOffset", 0, int.MaxValue, 0), limit = a.Number("textLimit", 120, 16000, 6000);
        int length = Math.Min(limit, Math.Max(0, text.Length - offset));
        return new { text = length == 0 ? "" : text.Substring(offset, length), textOffset = offset, textLength = text.Length,
            nextTextOffset = offset < text.Length - length ? (int?)(offset + length) : null };
    }

    private static object LetterDetails(JsonObject a)
    {
        var letter = GameLetter(a);
        return new { letter = LetterSummary(letter), body = TextPage(((IArchivable)letter).ArchivedTooltip, a) };
    }

    private static void RequireIdleDecision()
    {
        if (Find.Targeter.IsTargeting || Find.WorldTargeter.IsTargeting || Find.WindowStack.Windows.Any(w => w.forcePause || w is FloatMenu))
            throw new BridgeException("interaction_busy", "Resolve the current target or decision window first.");
    }

    private object OpenLetter(JsonObject a)
    {
        var letter = GameLetter(a);
        if (letter.Label.Resolve() != a.Text("expectedText", 4096)) throw new BridgeException("letter_stale", "Read the letter again; its label changed.");
        RequireIdleDecision();
        letter.OpenLetter();
        return new { opened = true, letterId = letter.GetUniqueLoadID(), dialogs = Dialogs(), next = "Resolve native choices using dialogs_list/dialog_choose or the visual GUI tools." };
    }

    private static bool PlayerVisibleQuest(Quest q) => q != null && !q.hidden && !q.hiddenInUI;
    private static IEnumerable<Quest> KnownQuests() => Find.QuestManager.QuestsListForReading.Where(PlayerVisibleQuest);
    private static Quest GameQuest(JsonObject a)
    {
        int id = a.Number("questId", 0, int.MaxValue);
        return KnownQuests().FirstOrDefault(q => q.id == id) ?? throw new BridgeException("quest_not_found", "No player-visible quest with this ID.");
    }
    private static object QuestSummary(Quest q) => new {
        questId = q.id, q.name, state = q.State.ToString(), successful = q.State == QuestState.EndedSuccess,
        historical = q.Historical, q.dismissed, q.appearanceTick, q.acceptanceTick, q.acceptanceExpireTick,
        ticksUntilExpiry = q.TicksUntilExpiry, q.challengeRating, q.charity, accepter = q.AccepterPawnLabelCap
    };
    private static object Quests(JsonObject a)
    {
        string filter = a["filter"] == null ? "all" : a.Text("filter", 16);
        if (filter is not ("all" or "available" or "active" or "historical")) throw new BridgeException("invalid_arguments", "filter must be all, available, active or historical.");
        return Page(KnownQuests().Where(q => filter == "all" || (filter == "available" ? q.State == QuestState.NotYetAccepted : filter == "active" ? q.State == QuestState.Ongoing : q.Historical))
            .OrderByDescending(q => q.appearanceTick).ThenByDescending(q => q.id).Select(QuestSummary), a);
    }
    private static string QuestDescription(Quest q)
    {
        // Match the normal tab: disabled/future quest parts must not reveal hidden outcomes.
        var sections = new[] { q.description.Resolve() }.Concat(q.PartsListForReading
            .OrderBy(p => p is QuestPartActivable active ? active.EnableTick : 0)
            .Where(p => p is not QuestPartActivable active || active.State == QuestPartState.Enabled)
            .Select(p => p.DescriptionPart));
        return string.Join("\n\n", sections.Where(s => !string.IsNullOrEmpty(s)));
    }
    private static QuestPart_Choice RewardPart(Quest q) => q.PartsListForReading.OfType<QuestPart_Choice>().FirstOrDefault();

    private static object RewardView(Reward reward)
    {
        try
        {
            // Use the virtual reward contract; no DLC-specific trackers, DefOfs or reward casts.
            return new { type = reward.GetType().FullName, description = reward.GetDescription(default), marketValue = reward.TotalMarketValue,
                descriptionAvailable = true, descriptionContext = "Generic reward description; native task text and GUI determine delivery details." };
        }
        catch (Exception e)
        {
            return new { type = reward.GetType().FullName, descriptionAvailable = false, needsVisualInspection = true, error = e.GetType().Name };
        }
    }
    private static object[] RewardChoices(Quest q) => RewardPart(q)?.choices.Select((c, i) => (object)new {
        choiceIndex = i, requiresAccepter = ChoiceRequiresAccepter(q, RewardPart(q), c), rewards = c.rewards.Select(RewardView).ToArray()
    }).ToArray() ?? Array.Empty<object>();

    private static bool ChoiceRequiresAccepter(Quest q, QuestPart_Choice part, QuestPart_Choice.Choice choice)
    {
        // This is the native reward button's remaining-parts rule, before Choose removes alternatives.
        var removed = part.choices.Where(c => c != choice).SelectMany(c => c.questParts).ToHashSet();
        removed.ExceptWith(choice.questParts);
        return q.PartsListForReading.Any(p => !removed.Contains(p) && p.RequiresAccepter);
    }
    private static string QuestSignature(Quest q, object[] rewards) => JsonSerializer.Serialize(new {
        q.name, state = q.State.ToString(), q.acceptanceExpireTick, description = q.description.Resolve(), rewards,
        choices = RewardPart(q)?.choices.Select(c => c.questParts.Select(p => p.GetUniqueLoadID()).ToArray()).ToArray()
    });
    private object QuestDetails(JsonObject a)
    {
        var q = GameQuest(a); var rewards = RewardChoices(q); var report = QuestUtility.CanAcceptQuest(q);
        string id = Guid.NewGuid().ToString("N");
        questObservations.AddOrUpdate(q, new(id, Current.Game, QuestSignature(q, rewards), DateTime.UtcNow.AddSeconds(60)));
        return new { quest = QuestSummary(q), tick = Find.TickManager.TicksGame, description = TextPage(QuestDescription(q), a),
            canAccept = q.State == QuestState.NotYetAccepted && report.Accepted,
            acceptanceReason = q.State != QuestState.NotYetAccepted ? q.State.ToString() : report.Reason,
            unmetRequirements = q.PartsListForReading.OfType<QuestPart_RequirementsToAccept>().Where(p => p.ShowInRequirementBox)
                .Select(p => p.CanAccept()).Where(r => !r.Accepted).Select(r => r.Reason).Distinct().ToArray(),
            requiresAccepter = q.RequiresAccepter, rewardChoices = rewards, observationId = id, observationExpiresSeconds = 60,
            royaltyActive = ModsConfig.RoyaltyActive, completionSource = "Quest.State (native game); no synthetic success or progress percentage." };
    }
    private static object OpenQuest(JsonObject a)
    {
        var q = GameQuest(a); RequireIdleDecision();
        Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Quests);
        ((MainTabWindow_Quests)MainButtonDefOf.Quests.TabWindow).Select(q);
        return new { opened = true, quest = QuestSummary(q) };
    }
    private object AcceptQuest(JsonObject a)
    {
        var q = GameQuest(a);
        if (!questObservations.TryGetValue(q, out var seen) || seen.Game != Current.Game || seen.Expires < DateTime.UtcNow
            || seen.Id != a.Text("observationId", 64) || seen.Signature != QuestSignature(q, RewardChoices(q)))
            throw new BridgeException("quest_stale", "Read quest_get again before accepting this quest/reward choice.");
        if (!Find.TickManager.Paused) throw new BridgeException("game_not_paused", "Pause before starting a quest decision.");
        RequireIdleDecision();
        ValidateQuestAcceptance(q, seen.Game);
        var part = RewardPart(q); QuestPart_Choice.Choice choice = null;
        if (part != null)
        {
            int index = a.Number("choiceIndex", 0, Math.Max(0, part.choices.Count - 1));
            if (index >= part.choices.Count) throw new BridgeException("reward_unavailable", "No native reward option is available.");
            choice = part.choices[index];
            if (!choice.rewards.Any(r => r.StackElements.Any())) throw new BridgeException("reward_unavailable", "The native tab does not display this reward option; use quest_open for custom UI.");
        }
        else if (a["choiceIndex"] != null) throw new BridgeException("invalid_arguments", "This quest has no reward choice.");
        bool requires = choice == null ? q.RequiresAccepter : ChoiceRequiresAccepter(q, part, choice);
        // A dedicated native tab instance keeps the pending accepter callbacks bound to this quest.
        var window = new MainTabWindow_Quests(); window.Select(q);
        NativeAcceptQuest(window, () => {
            ValidateQuestAcceptance(q, seen.Game);
            if (choice != null)
            {
                if (RewardPart(q) != part || !part.choices.Contains(choice)) throw new BridgeException("quest_stale", "Reward choices changed while awaiting confirmation.");
                part.Choose(choice);
            }
        }, requires);
        questObservations.Remove(q);
        return new { initiated = true, accepted = q.EverAccepted, quest = QuestSummary(q), dialogs = Dialogs(),
            next = "Resolve any native accepter/confirmation window, then quest_get to verify acceptance and eventual completion." };
    }
    private static void ValidateQuestAcceptance(Quest q, Game owner)
    {
        if (Current.Game != owner || !KnownQuests().Contains(q) || q.State != QuestState.NotYetAccepted)
            throw new BridgeException("quest_stale", "Quest is no longer available to accept.");
        var report = QuestUtility.CanAcceptQuest(q);
        if (!report.Accepted) throw new BridgeException("quest_rejected", report.Reason ?? "Native quest acceptance requirements are not met.");
    }
}
