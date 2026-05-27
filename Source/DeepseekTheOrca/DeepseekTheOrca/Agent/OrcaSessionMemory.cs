using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaMemoryEntry
    {
        public int tick;
        public string source;
        public string text;
    }

    public static class OrcaSessionMemory
    {
        private const int CompressThresholdChars = 14000;
        private const int KeepRecentAfterCompressChars = 4500;
        private const int MaxContextChars = 7000;
        private const int MaxSummaryChars = 3500;
        private static readonly object syncRoot = new object();
        private static readonly List<OrcaMemoryEntry> entries = new List<OrcaMemoryEntry>();
        private static readonly LlmApiClient client = new LlmApiClient();
        private static string compressedSummary = "";
        private static Task<LlmChatResponse> pendingCompression;
        private static int lastCompressionAttemptTick = -999999;

        public static void Add(string source, string text)
        {
            if (text.NullOrEmpty())
            {
                return;
            }

            lock (syncRoot)
            {
                entries.Add(new OrcaMemoryEntry
                {
                    tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame,
                    source = source ?? "",
                    text = Clamp(text.Trim(), 1600)
                });
            }
        }

        public static string ContextForPrompt()
        {
            lock (syncRoot)
            {
                if (compressedSummary.NullOrEmpty() && entries.Count == 0)
                {
                    return "";
                }

                StringBuilder builder = new StringBuilder();
                builder.AppendLine("Orca run memory. This is process-level memory for the current RimWorld launch, shared across saves, and reset when the game process restarts. Use it as soft context, not as immutable truth.");
                if (!compressedSummary.NullOrEmpty())
                {
                    builder.AppendLine("Compressed earlier memory:");
                    builder.AppendLine(Clamp(compressedSummary, MaxSummaryChars));
                }

                string recent = RecentEntriesText(MaxContextChars - builder.Length);
                if (!recent.NullOrEmpty())
                {
                    builder.AppendLine("Recent memory entries:");
                    builder.Append(recent);
                }

                return Clamp(builder.ToString(), MaxContextChars);
            }
        }

        public static void Tick()
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            if (settings == null || !settings.HasModelForRole(OrcaLlmModelRole.Dialogue))
            {
                return;
            }

            Task<LlmChatResponse> finishedTask = null;
            lock (syncRoot)
            {
                if (pendingCompression != null && pendingCompression.IsCompleted)
                {
                    finishedTask = pendingCompression;
                    pendingCompression = null;
                }
            }

            if (finishedTask != null)
            {
                CompleteCompression(finishedTask);
            }

            lock (syncRoot)
            {
                int ticksGame = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
                if (pendingCompression != null
                    || ticksGame - lastCompressionAttemptTick < 5000
                    || RecentEntriesText(int.MaxValue).Length < CompressThresholdChars)
                {
                    return;
                }

                lastCompressionAttemptTick = ticksGame;
                List<LlmChatMessage> messages = new List<LlmChatMessage>();
                messages.Add(LlmChatMessage.System(
                    "You compress Orca's process-level memory for a RimWorld mod. "
                    + "Preserve stable facts, player preferences, Orca's ongoing attitudes, unresolved story threads, important colony events, and useful context for future dialogue. "
                    + "Discard repetitive tool traces and low-value chatter. "
                    + "Return a concise plain-text memory summary, not JSON."));
                messages.Add(LlmChatMessage.User(BuildCompressionInput()));
                pendingCompression = client.SendPlainChatCompletionAsync(settings, messages, OrcaLlmModelRole.Dialogue);
                Debug("Started Orca memory compression.");
            }
        }

        private static void CompleteCompression(Task<LlmChatResponse> finishedTask)
        {
            LlmChatResponse response;
            try
            {
                response = finishedTask.Result;
            }
            catch (System.Exception ex)
            {
                Debug("Orca memory compression failed: " + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            if (response == null || !response.success)
            {
                Debug("Orca memory compression failed: " + (response == null ? "no response" : response.errorMessage));
                return;
            }

            lock (syncRoot)
            {
                compressedSummary = Clamp(response.content ?? "", MaxSummaryChars);
                KeepRecentEntriesLocked(KeepRecentAfterCompressChars);
            }

            Debug("Orca memory compressed.");
        }

        private static string BuildCompressionInput()
        {
            StringBuilder builder = new StringBuilder();
            if (!compressedSummary.NullOrEmpty())
            {
                builder.AppendLine("Existing compressed memory:");
                builder.AppendLine(compressedSummary);
                builder.AppendLine();
            }

            builder.AppendLine("Raw recent memory entries:");
            builder.Append(RecentEntriesText(int.MaxValue));
            return builder.ToString();
        }

        private static string RecentEntriesText(int maxChars)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                OrcaMemoryEntry entry = entries[i];
                string line = "tick " + entry.tick + " | " + entry.source + " | " + entry.text.Replace("\n", " ").Replace("\r", " ") + "\n";
                if (builder.Length + line.Length > maxChars)
                {
                    break;
                }

                builder.Insert(0, line);
            }

            return builder.ToString();
        }

        private static void KeepRecentEntriesLocked(int maxChars)
        {
            int chars = 0;
            List<OrcaMemoryEntry> kept = new List<OrcaMemoryEntry>();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                int entryChars = (entries[i].text == null ? 0 : entries[i].text.Length) + 64;
                if (chars + entryChars > maxChars)
                {
                    break;
                }

                chars += entryChars;
                kept.Add(entries[i]);
            }

            kept.Reverse();
            entries.Clear();
            entries.AddRange(kept);
        }

        private static string Clamp(string text, int maxChars)
        {
            if (text == null)
            {
                return "";
            }

            return text.Length <= maxChars ? text : text.Substring(0, maxChars) + "...";
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
