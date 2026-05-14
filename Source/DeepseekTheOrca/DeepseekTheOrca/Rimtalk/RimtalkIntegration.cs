using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca.Rimtalk
{
    public sealed class RimtalkHistorySnapshot
    {
        public string identityKey;
        public string playerName;
        public string gameLanguage;
        public string id;
        public string timestamp;
        public string origin;
        public string entryKind;
        public string channel;
        public string talkType;
        public string state;
        public string pawn;
        public string recipient;
        public string interactionType;
        public string prompt;
        public string response;
        public int createdTick;
        public int spokenTick;
    }

    public static class RimtalkIntegration
    {
        private const string RimtalkAssemblyName = "RimTalk";
        private const string RimtalkPackageId = "cj.rimtalk";

        public static bool IsAvailable
        {
            get { return IsRimtalkModActive() && ApiHistoryType() != null; }
        }

        public static AiToolResult GetChatHistory(Dictionary<string, string> arguments)
        {
            if (!IsAvailable)
            {
                return AiToolResult.Fail("RimTalk is not active");
            }

            Type apiHistoryType = ApiHistoryType();
            MethodInfo getAll = apiHistoryType == null ? null : apiHistoryType.GetMethod("GetAll", BindingFlags.Public | BindingFlags.Static);
            if (getAll == null)
            {
                return AiToolResult.Fail("RimTalk ApiHistory.GetAll is unavailable");
            }

            IEnumerable allLogs = getAll.Invoke(null, null) as IEnumerable;
            if (allLogs == null)
            {
                return AiToolResult.Ok("no RimTalk chat history").WithValue("entries", "[]");
            }

            int count = ParseInt(arguments, "count", 10, 1, 30);
            int maxChars = ParseInt(arguments, "maxChars", 500, 80, 2000);
            string originFilter = GetArgument(arguments, "origin");
            if (originFilter.NullOrEmpty())
            {
                originFilter = "all";
            }

            List<RimtalkChatRecord> records = new List<RimtalkChatRecord>();
            foreach (object log in allLogs)
            {
                RimtalkChatRecord record = RimtalkChatRecord.FromLog(log, maxChars);
                if (record != null && MatchesOrigin(record, originFilter))
                {
                    records.Add(record);
                }
            }

            records = records
                .OrderBy(record => record.SortTime)
                .ThenBy(record => record.CreatedTick)
                .ToList();

            if (records.Count > count)
            {
                records = records.GetRange(records.Count - count, count);
            }

            List<Dictionary<string, object>> entries = new List<Dictionary<string, object>>();
            for (int i = 0; i < records.Count; i++)
            {
                entries.Add(records[i].ToPayload());
            }

            return AiToolResult.Ok("RimTalk chat record count: " + entries.Count)
                .WithValue("playerName", GetPlayerName())
                .WithValue("gameLanguage", CurrentGameLanguage())
                .WithValue("originLegend", "player_initiated means TalkType User or Channel User; ai_auto_generated means RimTalk generated the dialogue without direct player input")
                .WithValue("entries", MiniJson.Serialize(entries));
        }

        public static bool TryGetRecentHistorySnapshots(int count, int maxChars, out List<RimtalkHistorySnapshot> snapshots, out string error)
        {
            snapshots = new List<RimtalkHistorySnapshot>();
            error = null;

            if (!IsAvailable)
            {
                error = "RimTalk is not active";
                return false;
            }

            Type apiHistoryType = ApiHistoryType();
            MethodInfo getAll = apiHistoryType == null ? null : apiHistoryType.GetMethod("GetAll", BindingFlags.Public | BindingFlags.Static);
            if (getAll == null)
            {
                error = "RimTalk ApiHistory.GetAll is unavailable";
                return false;
            }

            IEnumerable allLogs = getAll.Invoke(null, null) as IEnumerable;
            if (allLogs == null)
            {
                return true;
            }

            List<RimtalkChatRecord> records = new List<RimtalkChatRecord>();
            foreach (object log in allLogs)
            {
                RimtalkChatRecord record = RimtalkChatRecord.FromLog(log, maxChars);
                if (record != null)
                {
                    records.Add(record);
                }
            }

            records = records
                .OrderBy(record => record.SortTime)
                .ThenBy(record => record.CreatedTick)
                .ToList();

            if (records.Count > count)
            {
                records = records.GetRange(records.Count - count, count);
            }

            for (int i = 0; i < records.Count; i++)
            {
                snapshots.Add(records[i].ToSnapshot(GetPlayerName()));
            }

            return true;
        }

        private static bool MatchesOrigin(RimtalkChatRecord record, string originFilter)
        {
            if (originFilter == "all")
            {
                return true;
            }

            if (originFilter == "player_initiated")
            {
                return record.Origin == "player_initiated";
            }

            if (originFilter == "ai_auto_generated")
            {
                return record.Origin == "ai_auto_generated";
            }

            return true;
        }

        private static bool IsRimtalkModActive()
        {
            try
            {
                if (LoadedModManager.RunningModsListForReading == null)
                {
                    return FindRimtalkAssembly() != null;
                }

                foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading)
                {
                    string packageId = GetStringProperty(mod, "PackageIdPlayerFacing");
                    if (packageId.NullOrEmpty())
                    {
                        packageId = GetStringProperty(mod, "PackageIdNonUnique");
                    }

                    if (string.Equals(packageId, RimtalkPackageId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return FindRimtalkAssembly() != null;
            }

            return false;
        }

        private static Type ApiHistoryType()
        {
            Assembly assembly = FindRimtalkAssembly();
            return assembly == null ? null : assembly.GetType("RimTalk.Data.ApiHistory", false);
        }

        private static Assembly FindRimtalkAssembly()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                AssemblyName name = assembly.GetName();
                if (name != null && string.Equals(name.Name, RimtalkAssemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    return assembly;
                }
            }

            return null;
        }

        public static string GetPlayerName()
        {
            if (!IsAvailable)
            {
                return "";
            }

            Assembly assembly = FindRimtalkAssembly();
            Type settingsType = assembly == null ? null : assembly.GetType("RimTalk.Settings", false);
            MethodInfo get = settingsType == null ? null : settingsType.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
            if (get == null)
            {
                return "";
            }

            object settings = get.Invoke(null, null);
            object playerName = GetProperty(settings, "PlayerName");
            string text = playerName == null ? "" : playerName.ToString();
            return text.NullOrEmpty() ? "" : text;
        }

        public static string CurrentGameLanguage()
        {
            return OrcaLanguageUtility.CurrentGameLanguage();
        }

        private static string GetArgument(Dictionary<string, string> arguments, string key)
        {
            string value;
            return arguments != null && arguments.TryGetValue(key, out value) ? value : null;
        }

        private static int ParseInt(Dictionary<string, string> arguments, string key, int defaultValue, int min, int max)
        {
            string text = GetArgument(arguments, key);
            int value;
            if (text.NullOrEmpty() || !int.TryParse(text, out value))
            {
                value = defaultValue;
            }

            return Mathf.Clamp(value, min, max);
        }

        private static string GetStringProperty(object instance, string name)
        {
            object value = GetProperty(instance, name);
            return value == null ? null : value.ToString();
        }

        private static object GetProperty(object instance, string name)
        {
            if (instance == null)
            {
                return null;
            }

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null)
            {
                return property.GetValue(instance, null);
            }

            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return field == null ? null : field.GetValue(instance);
        }

        private sealed class RimtalkChatRecord
        {
            public DateTime SortTime;
            public string Id;
            public string IdentityKey;
            public string Timestamp;
            public string Origin;
            public string EntryKind;
            public string Channel;
            public string TalkType;
            public string State;
            public string Pawn;
            public string Recipient;
            public string InteractionType;
            public string Prompt;
            public string Response;
            public int ConversationId;
            public int CreatedTick;
            public int FinishedTick;
            public int SpokenTick;
            public bool IsFirstDialogue;
            public bool IsError;

            public static RimtalkChatRecord FromLog(object log, int maxChars)
            {
                if (log == null)
                {
                    return null;
                }

                object request = GetProperty(log, "TalkRequest");
                string channel = ValueText(GetProperty(log, "Channel"));
                string talkType = ValueText(GetProperty(request, "TalkType"));
                bool isPlayerInitiated = channel == "User" || talkType == "User";

                RimtalkChatRecord record = new RimtalkChatRecord();
                DateTime timestamp = GetDateTime(GetProperty(log, "Timestamp"));
                record.SortTime = timestamp == DateTime.MinValue ? DateTime.MaxValue : timestamp;
                record.Id = ValueText(GetProperty(log, "Id"));
                record.Timestamp = timestamp == DateTime.MinValue ? "" : timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                record.Channel = channel;
                record.TalkType = talkType;
                record.Origin = isPlayerInitiated ? "player_initiated" : "ai_auto_generated";
                record.EntryKind = channel == "User" ? "player_message" : "ai_response";
                record.State = InvokeString(log, "GetState");
                record.Pawn = FirstNonEmpty(ValueText(GetProperty(log, "Name")), PawnLabel(GetProperty(request, "Initiator")));
                record.Recipient = PawnLabel(GetProperty(request, "Recipient"));
                record.InteractionType = ValueText(GetProperty(log, "InteractionType"));
                record.Prompt = Truncate(FirstNonEmpty(ValueText(GetProperty(request, "RawPrompt")), ValueText(GetProperty(request, "Prompt"))), maxChars);
                record.Response = Truncate(ValueText(GetProperty(log, "Response")), maxChars);
                record.ConversationId = GetInt(GetProperty(log, "ConversationId"), -1);
                record.CreatedTick = GetInt(GetProperty(request, "CreatedTick"), -1);
                record.FinishedTick = GetInt(GetProperty(request, "FinishedTick"), -1);
                record.SpokenTick = GetInt(GetProperty(log, "SpokenTick"), -1);
                record.IsFirstDialogue = GetBool(GetProperty(log, "IsFirstDialogue"));
                record.IsError = GetBool(GetProperty(log, "IsError"));
                record.IdentityKey = BuildIdentityKey(record);

                if (record.Response.NullOrEmpty() && channel != "User")
                {
                    record.EntryKind = "pending_ai_request";
                }

                return record;
            }

            public Dictionary<string, object> ToPayload()
            {
                Dictionary<string, object> payload = new Dictionary<string, object>();
                payload["id"] = Id ?? "";
                payload["identityKey"] = IdentityKey ?? "";
                payload["playerName"] = GetPlayerName();
                payload["gameLanguage"] = CurrentGameLanguage();
                payload["timestamp"] = Timestamp ?? "";
                payload["origin"] = Origin ?? "";
                payload["entryKind"] = EntryKind ?? "";
                payload["channel"] = Channel ?? "";
                payload["talkType"] = TalkType ?? "";
                payload["state"] = State ?? "";
                payload["pawn"] = Pawn ?? "";
                payload["recipient"] = Recipient ?? "";
                payload["interactionType"] = InteractionType ?? "";
                payload["prompt"] = Prompt ?? "";
                payload["response"] = Response ?? "";
                payload["conversationId"] = ConversationId;
                payload["createdTick"] = CreatedTick;
                payload["finishedTick"] = FinishedTick;
                payload["spokenTick"] = SpokenTick;
                payload["isFirstDialogue"] = IsFirstDialogue;
                payload["isError"] = IsError;
                return payload;
            }

            public RimtalkHistorySnapshot ToSnapshot(string playerName)
            {
                return new RimtalkHistorySnapshot
                {
                    identityKey = IdentityKey ?? "",
                    playerName = playerName ?? "",
                    gameLanguage = CurrentGameLanguage(),
                    id = Id ?? "",
                    timestamp = Timestamp ?? "",
                    origin = Origin ?? "",
                    entryKind = EntryKind ?? "",
                    channel = Channel ?? "",
                    talkType = TalkType ?? "",
                    state = State ?? "",
                    pawn = Pawn ?? "",
                    recipient = Recipient ?? "",
                    interactionType = InteractionType ?? "",
                    prompt = Prompt ?? "",
                    response = Response ?? "",
                    createdTick = CreatedTick,
                    spokenTick = SpokenTick
                };
            }

            private static string BuildIdentityKey(RimtalkChatRecord record)
            {
                if (record == null)
                {
                    return "";
                }

                if (!record.Id.NullOrEmpty() && record.Id != Guid.Empty.ToString())
                {
                    return "id:" + record.Id;
                }

                return "fallback:"
                    + record.Timestamp + "|"
                    + record.CreatedTick + "|"
                    + record.SpokenTick + "|"
                    + record.Channel + "|"
                    + record.TalkType + "|"
                    + record.Pawn + "|"
                    + record.Recipient + "|"
                    + record.Prompt + "|"
                    + record.Response;
            }

            private static string InvokeString(object instance, string methodName)
            {
                if (instance == null)
                {
                    return "";
                }

                MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                if (method == null)
                {
                    return "";
                }

                object value = method.Invoke(instance, null);
                return ValueText(value);
            }

            private static string PawnLabel(object value)
            {
                Pawn pawn = value as Pawn;
                return pawn == null ? "" : pawn.LabelShort;
            }

            private static DateTime GetDateTime(object value)
            {
                return value is DateTime ? (DateTime)value : DateTime.MinValue;
            }

            private static int GetInt(object value, int defaultValue)
            {
                if (value is int)
                {
                    return (int)value;
                }

                int parsed;
                return value != null && int.TryParse(value.ToString(), out parsed) ? parsed : defaultValue;
            }

            private static bool GetBool(object value)
            {
                return value is bool && (bool)value;
            }

            private static string ValueText(object value)
            {
                return value == null ? "" : value.ToString();
            }

            private static string FirstNonEmpty(string first, string second)
            {
                return !first.NullOrEmpty() ? first : second;
            }

            private static string Truncate(string text, int maxChars)
            {
                if (text.NullOrEmpty() || text.Length <= maxChars)
                {
                    return text ?? "";
                }

                return text.Substring(0, maxChars - 3) + "...";
            }
        }
    }
}
