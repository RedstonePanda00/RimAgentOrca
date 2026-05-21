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
        private const int CooldownTicks = 9000;
        private const float DefaultPlayerInitiatedTriggerChance = 0.15f;
        private const float DefaultMissedRecordChanceBonus = 0.05f;
        private const int DefaultForceTriggerAfterMisses = 8;

        private readonly HashSet<string> seenRecordKeys = new HashSet<string>();
        private bool seeded;
        private int lastScanTick = -999999;
        private int lastTriggerTick = -999999;
        private int missedPlayerInitiatedRecords;
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
                    missedPlayerInitiatedRecords = 0;
                    Debug("RimTalk proactive trigger queued for " + Describe(record) + ".");
                    OrcaProactiveConversationManager.Enqueue(BuildRequest(record));
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

            if (record.origin != "player_initiated")
            {
                Debug("RimTalk proactive skipped non-player record: " + Describe(record) + ".");
                return false;
            }

            if (record.entryKind == "pending_ai_request")
            {
                Debug("RimTalk proactive skipped pending request: " + Describe(record) + ".");
                return false;
            }

            if (ticksGame - lastTriggerTick < CooldownTicks)
            {
                Debug("RimTalk proactive skipped cooldown: " + Describe(record) + ".");
                return false;
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            float baseChance = settings == null ? DefaultPlayerInitiatedTriggerChance : settings.rimtalkProactiveBaseChance;
            float missedBonus = settings == null ? DefaultMissedRecordChanceBonus : settings.rimtalkProactiveMissBonus;
            int forceAfterMisses = settings == null ? DefaultForceTriggerAfterMisses : settings.rimtalkProactiveForceAfterMisses;
            float chance = Mathf.Clamp01(baseChance + missedPlayerInitiatedRecords * missedBonus);
            if (missedPlayerInitiatedRecords >= forceAfterMisses)
            {
                chance = 1f;
            }

            bool triggered = Rand.Chance(chance);
            Debug("RimTalk proactive roll " + chance.ToStringPercent() + " for " + Describe(record) + ": " + (triggered ? "trigger" : "skip") + ".");
            if (!triggered)
            {
                missedPlayerInitiatedRecords++;
            }

            return triggered;
        }

        private static OrcaProactiveConversationRequest BuildRequest(RimtalkHistorySnapshot record)
        {
            string body = "A new RimTalk conversation record was observed.\n"
                + "RimTalk player name: " + (record.playerName.NullOrEmpty() ? "Player" : record.playerName) + "\n"
                + "Current game language: " + (record.gameLanguage.NullOrEmpty() ? RimtalkIntegration.CurrentGameLanguage() : record.gameLanguage) + "\n"
                + "Origin: " + (record.origin ?? "") + "\n"
                + "EntryKind: " + (record.entryKind ?? "") + "\n"
                + "Channel: " + (record.channel ?? "") + "\n"
                + "TalkType: " + (record.talkType ?? "") + "\n"
                + "Pawn: " + (record.pawn ?? "") + "\n"
                + "Recipient: " + (record.recipient ?? "") + "\n"
                + "Prompt: " + (record.prompt ?? "") + "\n"
                + "Response: " + (record.response ?? "") + "\n"
                + "Language instruction: reply in the current game language, not the English field labels in this trigger. "
                + "When this record mentions the player name, understand that it refers to the human player using RimTalk's configured player name. "
                + "You may call get_rimtalk_chat_history for nearby RimTalk context before speaking. "
                + "Speak proactively if the conversation gives you something interesting, teasing, useful, or story-relevant to say. "
                + "Do not claim every RimTalk line needs your attention.";

            return new OrcaProactiveConversationRequest("rimtalk_chat_history", "RimTalk conversation observed", body);
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
                Log.Message("[Deepseek The Orca] " + message);
            }
        }
    }
}
