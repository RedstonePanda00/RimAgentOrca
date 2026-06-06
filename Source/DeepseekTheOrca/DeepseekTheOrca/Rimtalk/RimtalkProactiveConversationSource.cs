using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca.Rimtalk
{
    public sealed class RimtalkProactiveConversationSource : IOrcaProactiveConversationSource
    {
        private const int ScanIntervalTicks = 250;
        private const int HistoryCount = 30;
        private const int MaxChars = 600;
        private const int DefaultCooldownTicks = 9000;
        private const int ContextDelayTicks = 1000;
        private const int ContextBeforeCount = 4;
        private const int ContextAfterCount = 4;
        private const float DefaultPlayerInitiatedTriggerChance = 0.15f;
        private const int DefaultForceTriggerAfterMisses = 8;

        private readonly HashSet<string> seenRecordKeys = new HashSet<string>();
        private bool seeded;
        private int lastScanTick = -999999;
        private int lastTriggerTick = -999999;
        private int missedConversationStarterRecords;
        private PendingRimtalkTrigger pendingTrigger;
        private string lastUnavailableMessage = "";

        public void Tick()
        {
            if (Find.TickManager == null)
            {
                return;
            }

            int ticksGame = Find.TickManager.TicksGame;
            if (ticksGame - lastScanTick < ScanIntervalTicks)
            {
                return;
            }

            lastScanTick = ticksGame;

            List<RimtalkHistorySnapshot> records;
            string error;
            if (!RimtalkIntegration.TryGetRecentHistorySnapshots(HistoryCount, MaxChars, out records, out error))
            {
                DebugOnce("RimTalk proactive source unavailable: " + (error ?? "unknown"));
                return;
            }
            lastUnavailableMessage = "";

            if (pendingTrigger != null && ticksGame >= pendingTrigger.dueTick)
            {
                Debug("RimTalk proactive context window queued for " + Describe(pendingTrigger.anchor) + ".");
                OrcaProactiveConversationManager.Enqueue(BuildRequest(pendingTrigger.anchor, records));
                MarkSeen(records);
                pendingTrigger = null;
                return;
            }
            if (pendingTrigger != null)
            {
                return;
            }

            if (!seeded)
            {
                MarkSeen(records);
                seeded = true;
                Debug("RimTalk proactive source seeded with " + records.Count + " recent record(s).");
                return;
            }

            for (int i = 0; i < records.Count; i++)
            {
                RimtalkHistorySnapshot record = records[i];
                string key = RecordKey(record);
                if (record == null || key.NullOrEmpty() || seenRecordKeys.Contains(key))
                {
                    continue;
                }

                seenRecordKeys.Add(key);
                if (ShouldTrigger(record, ticksGame))
                {
                    lastTriggerTick = ticksGame;
                    missedConversationStarterRecords = 0;
                    pendingTrigger = new PendingRimtalkTrigger(record, ticksGame + ContextDelayTicks);
                    Debug("RimTalk proactive trigger accepted for " + Describe(record) + "; waiting for context window.");
                    return;
                }
            }
        }

        private void MarkSeen(List<RimtalkHistorySnapshot> records)
        {
            for (int i = 0; i < records.Count; i++)
            {
                string key = RecordKey(records[i]);
                if (!key.NullOrEmpty())
                {
                    seenRecordKeys.Add(key);
                }
            }
        }

        private bool ShouldTrigger(RimtalkHistorySnapshot record, int ticksGame)
        {
            if (!OrcaProactiveConversationManager.AmbientEnabled)
            {
                Debug("RimTalk proactive skipped because ambient proactive dialogue is disabled: " + Describe(record) + ".");
                return false;
            }

            if (!IsConversationStarter(record))
            {
                Debug("RimTalk proactive skipped non-starter record: " + Describe(record) + ".");
                return false;
            }

            if (record.entryKind == "pending_ai_request")
            {
                Debug("RimTalk proactive skipped pending request: " + Describe(record) + ".");
                return false;
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            int cooldownTicks = settings == null ? DefaultCooldownTicks : settings.rimtalkProactiveCooldownTicks;
            if (cooldownTicks > 0 && ticksGame - lastTriggerTick < cooldownTicks)
            {
                Debug("RimTalk proactive skipped cooldown: " + Describe(record) + ".");
                return false;
            }

            float baseChance = settings == null ? DefaultPlayerInitiatedTriggerChance : settings.rimtalkProactiveBaseChance;
            int forceAfterMisses = settings == null ? DefaultForceTriggerAfterMisses : settings.rimtalkProactiveForceAfterMisses;
            float chance = Mathf.Clamp01(baseChance);
            if (forceAfterMisses > 0 && missedConversationStarterRecords >= forceAfterMisses)
            {
                chance = 1f;
            }

            bool triggered = Rand.Chance(chance);
            Debug("RimTalk proactive roll " + chance.ToStringPercent() + " for " + Describe(record) + ": " + (triggered ? "trigger" : "skip") + ".");
            if (!triggered)
            {
                missedConversationStarterRecords++;
            }

            return triggered;
        }

        private static bool IsConversationStarter(RimtalkHistorySnapshot record)
        {
            if (record == null)
            {
                return false;
            }

            if (record.channel == "User")
            {
                return true;
            }

            return record.channel == "Stream"
                && record.isFirstDialogue
                && record.talkType != "User"
                && record.entryKind != "pending_ai_request"
                && !record.response.NullOrEmpty();
        }

        private static OrcaProactiveConversationRequest BuildRequest(RimtalkHistorySnapshot record, List<RimtalkHistorySnapshot> records)
        {
            string body = "A RimTalk conversation starter was observed and selected for proactive commentary.\n"
                + "RimTalk player name: " + (record.playerName.NullOrEmpty() ? "Player" : record.playerName) + "\n"
                + "Current game language: " + (record.gameLanguage.NullOrEmpty() ? RimtalkIntegration.CurrentGameLanguage() : record.gameLanguage) + "\n"
                + "Trigger record:\n"
                + "Origin: " + (record.origin ?? "") + "\n"
                + "EntryKind: " + (record.entryKind ?? "") + "\n"
                + "Channel: " + (record.channel ?? "") + "\n"
                + "TalkType: " + (record.talkType ?? "") + "\n"
                + "State: " + (record.state ?? "") + "\n"
                + "IsFirstDialogue: " + record.isFirstDialogue + "\n"
                + "Pawn: " + (record.pawn ?? "") + "\n"
                + "Recipient: " + (record.recipient ?? "") + "\n"
                + "Prompt: " + (record.prompt ?? "") + "\n"
                + "Response: " + (record.response ?? "") + "\n"
                + "\nNearby RimTalk context:\n"
                + BuildContextWindow(record, records) + "\n"
                + "Language instruction: reply in the current game language, not the English field labels in this trigger. "
                + "When this record mentions the player name, understand that it refers to the human player using RimTalk's configured player name. "
                + "You may call get_rimtalk_chat_history for nearby RimTalk context before speaking. "
                + "Speak proactively if the conversation starter and nearby context give you something interesting, teasing, useful, or story-relevant to say. "
                + "Do not claim every RimTalk line needs your attention.";

            return new OrcaProactiveConversationRequest("rimtalk_chat_history", "RimTalk conversation observed", body);
        }

        private static string BuildContextWindow(RimtalkHistorySnapshot anchor, List<RimtalkHistorySnapshot> records)
        {
            if (anchor == null || records == null || records.Count == 0)
            {
                return "(no nearby RimTalk context)";
            }

            int anchorIndex = -1;
            string anchorKey = RecordKey(anchor);
            for (int i = 0; i < records.Count; i++)
            {
                if (RecordKey(records[i]) == anchorKey)
                {
                    anchorIndex = i;
                    break;
                }
            }

            if (anchorIndex < 0)
            {
                return FormatContextLine(anchor, true);
            }

            int start = Mathf.Max(0, anchorIndex - ContextBeforeCount);
            int end = Mathf.Min(records.Count - 1, anchorIndex + ContextAfterCount);
            List<string> lines = new List<string>();
            for (int i = start; i <= end; i++)
            {
                lines.Add(FormatContextLine(records[i], i == anchorIndex));
            }

            return string.Join("\n", lines.ToArray());
        }

        private static string FormatContextLine(RimtalkHistorySnapshot record, bool isAnchor)
        {
            if (record == null)
            {
                return "";
            }

            string text = record.channel == "User" ? record.response : record.response.NullOrEmpty() ? record.prompt : record.response;
            return (isAnchor ? "-> " : "   ")
                + "[" + (record.state ?? "") + " " + (record.channel ?? "") + "/" + (record.talkType ?? "") + "] "
                + (record.pawn ?? "")
                + (record.recipient.NullOrEmpty() ? "" : " -> " + record.recipient)
                + ": " + (text ?? "");
        }

        private static string RecordKey(RimtalkHistorySnapshot record)
        {
            if (record == null)
            {
                return "";
            }

            if (!record.identityKey.NullOrEmpty())
            {
                return record.identityKey;
            }

            if (!record.id.NullOrEmpty() && record.id != System.Guid.Empty.ToString())
            {
                return "id:" + record.id;
            }

            return "fallback:"
                + (record.timestamp ?? "") + "|"
                + record.createdTick + "|"
                + record.spokenTick + "|"
                + (record.channel ?? "") + "|"
                + (record.talkType ?? "") + "|"
                + (record.pawn ?? "") + "|"
                + (record.recipient ?? "") + "|"
                + (record.prompt ?? "") + "|"
                + (record.response ?? "");
        }

        private static string Describe(RimtalkHistorySnapshot record)
        {
            if (record == null)
            {
                return "<null>";
            }

            return "origin=" + (record.origin ?? "")
                + ", kind=" + (record.entryKind ?? "")
                + ", channel=" + (record.channel ?? "")
                + ", talkType=" + (record.talkType ?? "")
                + ", pawn=" + (record.pawn ?? "")
                + ", recipient=" + (record.recipient ?? "");
        }

        private void DebugOnce(string message)
        {
            if (message == lastUnavailableMessage)
            {
                return;
            }

            lastUnavailableMessage = message;
            Debug(message);
        }

        private static void Debug(string message)
        {
            if (DeepseekTheOrcaMod.Settings != null && DeepseekTheOrcaMod.Settings.debugLogging)
            {
                Log.Message("[RimAgent] " + message);
            }
        }

        private sealed class PendingRimtalkTrigger
        {
            public readonly RimtalkHistorySnapshot anchor;
            public readonly int dueTick;

            public PendingRimtalkTrigger(RimtalkHistorySnapshot anchor, int dueTick)
            {
                this.anchor = anchor;
                this.dueTick = dueTick;
            }
        }
    }
}
