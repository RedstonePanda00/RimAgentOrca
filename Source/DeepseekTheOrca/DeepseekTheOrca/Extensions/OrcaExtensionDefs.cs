using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public class OrcaChatPersonaDef : Def
    {
        public string prompt = "";
        public string narrativeTendency = "";
        public string controllerRoutingTendency = "";
        [MustTranslate]
        public string storytellerLabel = "";
        [MustTranslate]
        public string storytellerDescription = "";
        public string storytellerPortraitFolder = "";
        public string storytellerPortraitLargeName = "";
        public string storytellerPortraitTinyName = "";
        public string storytellerPortraitLargePath = "";
        public string storytellerPortraitTinyPath = "";
    }

    public class OrcaDefaultPersonaDef : Def
    {
        public string personaDefName = "";
        public int priority;
    }

    public sealed class OrcaChatWindowContext
    {
        public readonly OrcaChatSession session;
        public readonly Rect windowRect;
        public readonly Rect chatRect;
        public readonly Rect extensionRect;
        public readonly float alpha;

        public OrcaChatWindowContext(OrcaChatSession session, Rect windowRect, Rect chatRect, Rect extensionRect, float alpha)
        {
            this.session = session;
            this.windowRect = windowRect;
            this.chatRect = chatRect;
            this.extensionRect = extensionRect;
            this.alpha = alpha;
        }

        public bool IsWaiting
        {
            get { return session != null && session.IsWaiting; }
        }

        public string LastReplyText
        {
            get { return session == null ? "" : session.LastReplyText; }
        }

        public string LastUserText
        {
            get { return session == null ? "" : session.LastUserText; }
        }

        public string LastProcessText
        {
            get { return session == null ? "" : session.LastProcessText; }
        }

        public string LastErrorText
        {
            get { return session == null ? "" : session.LastErrorText; }
        }
    }

    public sealed class OrcaMainTabStatusContext
    {
        public readonly OrcaChatSession session;
        public readonly Rect inRect;
        public float y;

        public OrcaMainTabStatusContext(OrcaChatSession session, Rect inRect, float y)
        {
            this.session = session;
            this.inRect = inRect;
            this.y = y;
        }

        public void Advance(float height)
        {
            y += Mathf.Max(0f, height);
        }
    }

    public sealed class OrcaChatTurnContext
    {
        public readonly OrcaChatSession session;
        public readonly string source;
        public readonly string playerName;
        public readonly string text;
        public readonly List<string> contextTags;
        public readonly bool proactive;
        private readonly List<string> processLines = new List<string>();

        public OrcaChatTurnContext(OrcaChatSession session, string source, string playerName, string text, List<string> contextTags, bool proactive)
        {
            this.session = session;
            this.source = source ?? "";
            this.playerName = playerName ?? "";
            this.text = text ?? "";
            this.contextTags = contextTags ?? new List<string>();
            this.proactive = proactive;
        }

        public List<string> ProcessLines
        {
            get { return processLines; }
        }

        public void AddProcess(string line)
        {
            if (!line.NullOrEmpty())
            {
                processLines.Add(line);
            }
        }
    }

    public sealed class OrcaControllerRoutingContext
    {
        public readonly OrcaChatSession session;
        public readonly string latestUserText;
        public readonly bool isReview;
        public readonly int toolRoundsUsed;
        public readonly int maxToolGatheringRounds;
        public readonly int toolCallsUsed;
        public readonly int maxToolCalls;
        public readonly bool specialistReturnedNoToolCalls;

        public OrcaControllerRoutingContext(
            OrcaChatSession session,
            string latestUserText,
            bool isReview,
            int toolRoundsUsed,
            int maxToolGatheringRounds,
            int toolCallsUsed,
            int maxToolCalls,
            bool specialistReturnedNoToolCalls)
        {
            this.session = session;
            this.latestUserText = latestUserText ?? "";
            this.isReview = isReview;
            this.toolRoundsUsed = toolRoundsUsed;
            this.maxToolGatheringRounds = maxToolGatheringRounds;
            this.toolCallsUsed = toolCallsUsed;
            this.maxToolCalls = maxToolCalls;
            this.specialistReturnedNoToolCalls = specialistReturnedNoToolCalls;
        }
    }

    public sealed class OrcaChatReplyContext
    {
        public readonly OrcaChatSession session;
        public readonly OrcaChatReply reply;
        public readonly string originalContent;
        private readonly List<string> processLines = new List<string>();
        private readonly List<string> memoryFragments = new List<string>();

        public OrcaChatReplyContext(OrcaChatSession session, OrcaChatReply reply, string originalContent)
        {
            this.session = session;
            this.reply = reply;
            this.originalContent = originalContent ?? "";
        }

        public List<string> ProcessLines
        {
            get { return processLines; }
        }

        public List<string> MemoryFragments
        {
            get { return memoryFragments; }
        }

        public void AddProcess(string line)
        {
            if (!line.NullOrEmpty())
            {
                processLines.Add(line);
            }
        }

        public void AddMemoryFragment(string fragment)
        {
            if (!fragment.NullOrEmpty())
            {
                memoryFragments.Add(fragment);
            }
        }
    }

    public sealed class OrcaExecutionGateContext
    {
        private readonly List<string> processLines = new List<string>();

        public readonly OrcaChatSession session;
        public readonly string toolName;
        public readonly Dictionary<string, string> arguments;

        public OrcaExecutionGateContext(OrcaChatSession session, string toolName, Dictionary<string, string> arguments)
        {
            this.session = session;
            this.toolName = toolName ?? "";
            this.arguments = arguments ?? new Dictionary<string, string>();
        }

        public bool Blocked { get; private set; }
        public string BlockReason { get; private set; }

        public List<string> ProcessLines
        {
            get { return processLines; }
        }

        public void AddProcess(string line)
        {
            if (!line.NullOrEmpty())
            {
                processLines.Add(line);
            }
        }

        public void Block(string reason)
        {
            Blocked = true;
            BlockReason = reason.NullOrEmpty() ? "execution tool was blocked by an extension" : reason;
        }
    }

    public abstract class OrcaExtensionWorker
    {
        public OrcaExtensionDef def;

        public virtual void Register(OrcaExtensionRegistry registry)
        {
        }
    }

    public abstract class OrcaExtensionSettingsWorker
    {
        public OrcaExtensionDef def;

        public virtual void DrawSettings(Rect rect, OrcaSettingsContext context)
        {
        }

        public virtual Vector2 WindowSize
        {
            get { return new Vector2(700f, 520f); }
        }
    }

    public class OrcaExtensionSettingEntry
    {
        public string key = "";
        public string fieldName = "";
        public string type = "string";
        public string label = "";
        public string tooltip = "";
        public float min;
        public float max = 1f;
        public List<string> options = new List<string>();
        public bool clearChatOnChange = true;
    }

    public class OrcaExtensionDef : Def
    {
        public bool defaultEnabled = true;
        public string author = "";
        public string category = "";
        public string details = "";
        public List<string> capabilities = new List<string>();
        public List<string> permissions = new List<string>();
        public List<OrcaExtensionSettingEntry> settings = new List<OrcaExtensionSettingEntry>();
        public float order;
        public Type workerClass;
        public Type settingsWorkerClass;

        private OrcaExtensionWorker workerInt;
        private OrcaExtensionSettingsWorker settingsWorkerInt;

        public OrcaExtensionWorker Worker
        {
            get
            {
                if (workerInt != null)
                {
                    return workerInt;
                }

                if (workerClass == null)
                {
                    return null;
                }

                workerInt = Activator.CreateInstance(workerClass) as OrcaExtensionWorker;
                if (workerInt != null)
                {
                    workerInt.def = this;
                }

                return workerInt;
            }
        }

        public OrcaExtensionSettingsWorker SettingsWorker
        {
            get
            {
                if (settingsWorkerInt != null)
                {
                    return settingsWorkerInt;
                }

                if (settingsWorkerClass == null)
                {
                    return null;
                }

                settingsWorkerInt = Activator.CreateInstance(settingsWorkerClass) as OrcaExtensionSettingsWorker;
                if (settingsWorkerInt != null)
                {
                    settingsWorkerInt.def = this;
                }

                return settingsWorkerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (workerClass == null)
            {
                yield return "workerClass must be set.";
            }
            else if (!typeof(OrcaExtensionWorker).IsAssignableFrom(workerClass))
            {
                yield return "workerClass must inherit from DeepseekTheOrca.OrcaExtensionWorker.";
            }

            if (settingsWorkerClass != null && !typeof(OrcaExtensionSettingsWorker).IsAssignableFrom(settingsWorkerClass))
            {
                yield return "settingsWorkerClass must inherit from DeepseekTheOrca.OrcaExtensionSettingsWorker.";
            }
        }
    }

    public class OrcaToolParameterDef
    {
        public string name = "";
        public string type = "string";
        public string description = "";
        public bool required;
        public List<string> enumValues = new List<string>();
    }

    public abstract class OrcaToolWorker
    {
        public OrcaToolDef def;

        public virtual bool ShouldRegister()
        {
            return true;
        }

        public virtual bool CanUse(AiToolContext context, out string reason)
        {
            reason = "";
            return true;
        }

        public abstract AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments);

        // Execution tools (def.isExecutionTool) override this to perform the
        // real action after Invoke has validated and the extension gate passed.
        public virtual AiToolResult ExecuteValidated(AiToolContext context, Dictionary<string, string> arguments, List<string> processLines)
        {
            return AiToolResult.Fail("tool does not implement execution: " + (def == null ? GetType().Name : def.ToolName));
        }
    }

    public class OrcaToolDef : Def
    {
        public string toolName = "";
        public Type workerClass;
        public bool exposeToChat = true;
        public bool exposeToStorytellerPlanning;
        public bool requiresCurrentMap = true;
        public bool allowDuringProactive = true;
        public bool isExecutionTool;
        public bool defaultEnabled = true;
        public List<OrcaToolParameterDef> parameters = new List<OrcaToolParameterDef>();

        private OrcaToolWorker workerInt;

        public string ToolName
        {
            get { return toolName.NullOrEmpty() ? defName : toolName; }
        }

        public string ToolDescription
        {
            get { return description.NullOrEmpty() ? label.NullOrEmpty() ? ToolName : label : description; }
        }

        public OrcaToolWorker Worker
        {
            get
            {
                if (workerInt != null)
                {
                    return workerInt;
                }

                if (workerClass == null)
                {
                    return null;
                }

                workerInt = Activator.CreateInstance(workerClass) as OrcaToolWorker;
                if (workerInt != null)
                {
                    workerInt.def = this;
                }

                return workerInt;
            }
        }

        public Dictionary<string, object> BuildInputSchema()
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            List<object> required = new List<object>();
            List<OrcaToolParameterDef> items = parameters ?? new List<OrcaToolParameterDef>();
            for (int i = 0; i < items.Count; i++)
            {
                OrcaToolParameterDef parameter = items[i];
                if (parameter == null || parameter.name.NullOrEmpty())
                {
                    continue;
                }

                Dictionary<string, object> property = new Dictionary<string, object>();
                property["type"] = NormalizeParameterType(parameter.type);
                if (!parameter.description.NullOrEmpty())
                {
                    property["description"] = parameter.description;
                }
                if (parameter.enumValues != null && parameter.enumValues.Count > 0)
                {
                    property["enum"] = new List<object>(parameter.enumValues.ToArray());
                }

                properties[parameter.name] = property;
                if (parameter.required)
                {
                    required.Add(parameter.name);
                }
            }

            Dictionary<string, object> schema = new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", properties }
            };
            if (required.Count > 0)
            {
                schema["required"] = required;
            }

            return schema;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (ToolName.NullOrEmpty())
            {
                yield return "toolName or defName must be set.";
            }

            if (workerClass == null)
            {
                yield return "workerClass must be set.";
            }
            else if (!typeof(OrcaToolWorker).IsAssignableFrom(workerClass))
            {
                yield return "workerClass must inherit from DeepseekTheOrca.OrcaToolWorker.";
            }

            if (parameters == null)
            {
                yield break;
            }

            for (int i = 0; i < parameters.Count; i++)
            {
                OrcaToolParameterDef parameter = parameters[i];
                if (parameter == null)
                {
                    yield return "parameters contains a null entry.";
                    continue;
                }

                if (parameter.name.NullOrEmpty())
                {
                    yield return "tool parameter at index " + i + " has no name.";
                }
            }
        }

        private static string NormalizeParameterType(string type)
        {
            switch ((type ?? "").Trim().ToLowerInvariant())
            {
                case "integer":
                case "number":
                case "boolean":
                case "object":
                case "array":
                    return type.Trim().ToLowerInvariant();
                default:
                    return "string";
            }
        }
    }
}
