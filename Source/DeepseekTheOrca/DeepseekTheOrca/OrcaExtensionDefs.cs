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
        public string storytellerLabel = "";
        public string storytellerDescription = "";
        public string storytellerPortraitFolder = "";
        public string storytellerPortraitLargeName = "";
        public string storytellerPortraitTinyName = "";
        public string storytellerPortraitLargePath = "";
        public string storytellerPortraitTinyPath = "";
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

        public int Mood
        {
            get { return session == null ? 60 : session.Mood; }
        }

        public int LastMoodDelta
        {
            get { return session == null ? 0 : session.LastMoodDelta; }
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

    public abstract class OrcaExtensionWorker
    {
        public OrcaExtensionDef def;

        public virtual void OnEnabled()
        {
        }

        public virtual void OnDisabled()
        {
        }

        public virtual void AppendSystemPrompt(StringBuilder builder)
        {
        }

        public virtual string ControllerRoutingHint()
        {
            return "";
        }

        public virtual float GetChatWindowExtraWidth(OrcaChatWindowContext context)
        {
            return 0f;
        }

        public virtual void DrawChatWindow(Rect rect, OrcaChatWindowContext context)
        {
        }

        public virtual void DrawChatWindowOverlay(Rect windowRect, OrcaChatWindowContext context)
        {
        }

        public virtual OrcaReplyDisplayController CreateReplyDisplayController(string fullText, OrcaChatSession session)
        {
            return null;
        }

        public virtual void DrawSettings(Rect rect)
        {
        }
    }

    public abstract class OrcaReplyDisplayController
    {
        public abstract string VisibleText { get; }
        public abstract bool IsComplete { get; }
        public abstract void Tick();
        public abstract void Finish();
    }

    public class OrcaExtensionDef : Def
    {
        public bool defaultEnabled = true;
        public string category = "";
        public string details = "";
        public float order;
        public Type workerClass;

        private OrcaExtensionWorker workerInt;

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
