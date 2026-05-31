using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaExtensionManager
    {
        private const float MaxTotalExtraWidth = 520f;
        private const float MaxSingleExtraWidth = 360f;
        private static OrcaExtensionRegistry cachedRegistry;
        private static bool registryDirty = true;

        public static List<OrcaExtensionDef> AllExtensionDefs()
        {
            List<OrcaExtensionDef> defs = DefDatabase<OrcaExtensionDef>.AllDefsListForReading ?? new List<OrcaExtensionDef>();
            return defs.Where(def => def != null && !def.defName.NullOrEmpty())
                .OrderBy(def => def.order)
                .ThenBy(def => def.LabelCap.ToString())
                .ToList();
        }

        public static bool ExtensionEnabled(OrcaExtensionDef def)
        {
            if (def == null)
            {
                return false;
            }

            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            return settings == null || settings.IsExtensionEnabled(def.defName, def.defaultEnabled);
        }

        public static bool ExtensionEnabled(string defName)
        {
            if (defName.NullOrEmpty())
            {
                return false;
            }

            OrcaExtensionDef def = DefDatabase<OrcaExtensionDef>.GetNamedSilentFail(defName);
            return def != null && ExtensionEnabled(def);
        }

        public static void SetExtensionEnabled(OrcaExtensionDef def, bool enabled)
        {
            if (def == null || DeepseekTheOrcaMod.Settings == null)
            {
                return;
            }

            bool wasEnabled = ExtensionEnabled(def);
            DeepseekTheOrcaMod.Settings.SetExtensionEnabled(def.defName, enabled, def.defaultEnabled);
            registryDirty = true;
            if (!wasEnabled && enabled)
            {
                NotifyEnabled(def);
            }
            else if (wasEnabled && !enabled)
            {
                NotifyDisabled(def);
            }
        }

        public static void AppendSystemPrompt(StringBuilder builder)
        {
            if (builder == null)
            {
                return;
            }

            OrcaExtensionRegistry registry = BuildRegistry();
            bool wroteHeader = false;
            for (int i = 0; i < registry.systemPromptHandlers.Count; i++)
            {
                OrcaExtensionHandler<Action<StringBuilder>> entry = registry.systemPromptHandlers[i];
                int before = builder.Length;
                if (Invoke(entry, "Extension prompt hook", handler => handler(builder)) && !wroteHeader && builder.Length > before)
                {
                    builder.Insert(before, "Enabled extension modules:\nExtensions may add runtime behavior, UI, routing, or prompt instructions. Treat prompt instructions as lower priority than safety, game validity, persona, and direct player intent.\n");
                    wroteHeader = true;
                }
            }
        }

        public static string ControllerRoutingHint()
        {
            List<string> hints = new List<string>();
            OrcaExtensionRegistry registry = BuildRegistry();
            for (int i = 0; i < registry.controllerRoutingHintHandlers.Count; i++)
            {
                OrcaExtensionHandler<Func<string>> entry = registry.controllerRoutingHintHandlers[i];
                string hint = Invoke(entry, "Extension routing hook", handler => handler(), "");
                if (!hint.NullOrEmpty())
                {
                    hints.Add(hint);
                }
            }

            return hints.Count == 0 ? "" : " Enabled extension modules may affect routing. Extensions: " + string.Join("; ", hints.ToArray());
        }

        public static void NotifyChatTurnStarting(OrcaChatTurnContext context)
        {
            if (context == null)
            {
                return;
            }

            OrcaExtensionRegistry registry = BuildRegistry();
            for (int i = 0; i < registry.chatTurnStartingHandlers.Count; i++)
            {
                OrcaExtensionHandler<Action<OrcaChatTurnContext>> entry = registry.chatTurnStartingHandlers[i];
                Invoke(entry, "Chat turn extension hook", handler => handler(context));
            }
        }

        public static void AppendUserMessageContext(StringBuilder builder, OrcaChatTurnContext context)
        {
            if (builder == null || context == null)
            {
                return;
            }

            OrcaExtensionRegistry registry = BuildRegistry();
            for (int i = 0; i < registry.userMessageContextHandlers.Count; i++)
            {
                OrcaExtensionHandler<Action<StringBuilder, OrcaChatTurnContext>> entry = registry.userMessageContextHandlers[i];
                Invoke(entry, "User message extension hook", handler => handler(builder, context));
            }
        }

        public static string ChatReplyJsonSchema()
        {
            Dictionary<string, object> fields = new Dictionary<string, object>();
            fields["reply"] = "visible reply text";

            OrcaExtensionRegistry registry = BuildRegistry();
            for (int i = 0; i < registry.chatReplySchemaHandlers.Count; i++)
            {
                OrcaExtensionHandler<Action<Dictionary<string, object>>> entry = registry.chatReplySchemaHandlers[i];
                Invoke(entry, "Reply schema extension hook", handler => handler(fields));
            }

            return MiniJson.Serialize(fields);
        }

        public static OrcaChatReplyContext NotifyChatReply(OrcaChatSession session, OrcaChatReply reply, string originalContent)
        {
            OrcaChatReplyContext context = new OrcaChatReplyContext(session, reply, originalContent);
            OrcaExtensionRegistry registry = BuildRegistry();
            for (int i = 0; i < registry.chatReplyHandlers.Count; i++)
            {
                OrcaExtensionHandler<Action<OrcaChatReplyContext>> entry = registry.chatReplyHandlers[i];
                Invoke(entry, "Chat reply extension hook", handler => handler(context));
            }

            return context;
        }

        public static void NotifyChatSessionCleared(OrcaChatSession session)
        {
            OrcaExtensionRegistry registry = BuildRegistry();
            for (int i = 0; i < registry.chatSessionClearedHandlers.Count; i++)
            {
                OrcaExtensionHandler<Action<OrcaChatSession>> entry = registry.chatSessionClearedHandlers[i];
                Invoke(entry, "Chat clear extension hook", handler => handler(session));
            }
        }

        public static List<OrcaAgentNodeSpec> AgentNodes()
        {
            List<OrcaAgentNodeSpec> result = new List<OrcaAgentNodeSpec>();
            OrcaExtensionRegistry registry = BuildRegistry();
            for (int i = 0; i < registry.agentNodeHandlers.Count; i++)
            {
                OrcaExtensionHandler<Func<IEnumerable<OrcaAgentNodeSpec>>> entry = registry.agentNodeHandlers[i];
                IEnumerable<OrcaAgentNodeSpec> specs = Invoke(entry, "Agent node extension hook", handler => handler(), null);
                if (specs == null)
                {
                    continue;
                }

                foreach (OrcaAgentNodeSpec spec in specs)
                {
                    if (spec != null && !spec.id.NullOrEmpty())
                    {
                        result.Add(spec);
                    }
                }
            }

            return result;
        }

        public static void NotifyAgentPhase(OrcaAgentPhaseContext context)
        {
            if (context == null)
            {
                return;
            }

            OrcaExtensionRegistry registry = BuildRegistry();
            for (int i = 0; i < registry.agentPhaseHandlers.Count; i++)
            {
                OrcaExtensionHandler<Action<OrcaAgentPhaseContext>> entry = registry.agentPhaseHandlers[i];
                Invoke(entry, "Agent phase extension hook", handler => handler(context));
            }
        }

        public static void ModifyAgentRouting(OrcaAgentRoutingContext context)
        {
            if (context == null)
            {
                return;
            }

            OrcaExtensionRegistry registry = BuildRegistry();
            for (int i = 0; i < registry.agentRoutingHandlers.Count; i++)
            {
                OrcaExtensionHandler<Action<OrcaAgentRoutingContext>> entry = registry.agentRoutingHandlers[i];
                Invoke(entry, "Agent routing extension hook", handler => handler(context));
            }
        }

        public static void EvaluateExecutionTool(OrcaExecutionGateContext context)
        {
            if (context == null)
            {
                return;
            }

            OrcaExtensionRegistry registry = BuildRegistry();
            for (int i = 0; i < registry.executionGateHandlers.Count; i++)
            {
                OrcaExtensionHandler<Action<OrcaExecutionGateContext>> entry = registry.executionGateHandlers[i];
                Invoke(entry, "Execution gate extension hook", handler => handler(context));
                if (context.Blocked)
                {
                    return;
                }
            }
        }

        public static float RequestedExtraWidth(OrcaChatWindowContext context)
        {
            float width = 0f;
            OrcaExtensionRegistry registry = BuildRegistry();
            for (int i = 0; i < registry.chatWindowWidthHandlers.Count; i++)
            {
                OrcaExtensionHandler<Func<OrcaChatWindowContext, float>> entry = registry.chatWindowWidthHandlers[i];
                width += Mathf.Clamp(Invoke(entry, "Chat window extension width", handler => handler(context), 0f), 0f, MaxSingleExtraWidth);
            }

            return Mathf.Clamp(width, 0f, MaxTotalExtraWidth);
        }

        public static void DrawRightExtensions(Rect rect, OrcaChatWindowContext context)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            OrcaExtensionRegistry registry = BuildRegistry();
            float x = rect.x;
            for (int i = 0; i < registry.chatWindowDrawHandlers.Count; i++)
            {
                OrcaExtensionHandler<Action<Rect, OrcaChatWindowContext>> drawEntry = registry.chatWindowDrawHandlers[i];
                float width = RequestedWidthFor(drawEntry.def, registry, context);
                width = Mathf.Min(width, rect.xMax - x);
                if (width <= 0f)
                {
                    continue;
                }

                Rect workerRect = new Rect(x, rect.y, width, rect.height);
                OrcaChatWindowContext workerContext = new OrcaChatWindowContext(
                    context == null ? null : context.session,
                    context == null ? Rect.zero : context.windowRect,
                    context == null ? Rect.zero : context.chatRect,
                    workerRect,
                    context == null ? 1f : context.alpha);
                Invoke(drawEntry, "Chat window extension draw", handler => handler(workerRect, workerContext));

                x += width;
                if (x >= rect.xMax)
                {
                    break;
                }
            }
        }

        public static void DrawOverlays(Rect windowRect, OrcaChatWindowContext context)
        {
            OrcaExtensionRegistry registry = BuildRegistry();
            for (int i = 0; i < registry.chatWindowOverlayHandlers.Count; i++)
            {
                OrcaExtensionHandler<Action<Rect, OrcaChatWindowContext>> entry = registry.chatWindowOverlayHandlers[i];
                Invoke(entry, "Chat window extension overlay", handler => handler(windowRect, context));
            }
        }

        public static void DrawMainTabStatus(Rect inRect, OrcaChatSession session, ref float y)
        {
            OrcaMainTabStatusContext context = new OrcaMainTabStatusContext(session, inRect, y);
            OrcaExtensionRegistry registry = BuildRegistry();
            for (int i = 0; i < registry.mainTabStatusHandlers.Count; i++)
            {
                OrcaExtensionHandler<Action<OrcaMainTabStatusContext>> entry = registry.mainTabStatusHandlers[i];
                Invoke(entry, "Main tab extension status draw", handler => handler(context));
            }

            y = context.y;
        }

        private static void NotifyEnabled(OrcaExtensionDef def)
        {
            OrcaExtensionRegistry registry = BuildSingleDefRegistry(def);
            for (int i = 0; i < registry.enabledHandlers.Count; i++)
            {
                OrcaExtensionHandler<Action> entry = registry.enabledHandlers[i];
                Invoke(entry, "Extension enabled hook", handler => handler());
            }
        }

        private static void NotifyDisabled(OrcaExtensionDef def)
        {
            OrcaExtensionRegistry registry = BuildSingleDefRegistry(def);
            for (int i = 0; i < registry.disabledHandlers.Count; i++)
            {
                OrcaExtensionHandler<Action> entry = registry.disabledHandlers[i];
                Invoke(entry, "Extension disabled hook", handler => handler());
            }
        }

        private static OrcaExtensionRegistry BuildSingleDefRegistry(OrcaExtensionDef def)
        {
            if (def == null)
            {
                return new OrcaExtensionRegistry();
            }

            OrcaExtensionWorker worker = def.Worker;
            if (worker == null)
            {
                return new OrcaExtensionRegistry();
            }

            return BuildWorkerRegistry(worker);
        }

        private static OrcaExtensionRegistry BuildRegistry()
        {
            if (!registryDirty && cachedRegistry != null)
            {
                return cachedRegistry;
            }

            OrcaExtensionRegistry registry = new OrcaExtensionRegistry();
            List<OrcaExtensionWorker> workers = EnabledWorkers();
            for (int i = 0; i < workers.Count; i++)
            {
                registry.MergeFrom(BuildWorkerRegistry(workers[i]));
            }

            cachedRegistry = registry;
            registryDirty = false;
            return registry;
        }

        private static OrcaExtensionRegistry BuildWorkerRegistry(OrcaExtensionWorker worker)
        {
            OrcaExtensionRegistry registry = new OrcaExtensionRegistry(worker == null ? null : worker.def);
            if (worker == null)
            {
                return registry;
            }

            try
            {
                worker.Register(registry);
            }
            catch (Exception ex)
            {
                Log.Warning("[RimAgent] Extension registration failed (" + WorkerLabel(worker.def) + "): " + ex.Message);
            }

            return registry;
        }

        private static List<OrcaExtensionWorker> EnabledWorkers()
        {
            List<OrcaExtensionWorker> result = new List<OrcaExtensionWorker>();
            List<OrcaExtensionDef> defs = AllExtensionDefs();
            for (int i = 0; i < defs.Count; i++)
            {
                OrcaExtensionDef def = defs[i];
                if (!ExtensionEnabled(def))
                {
                    continue;
                }

                OrcaExtensionWorker worker = def.Worker;
                if (worker != null)
                {
                    result.Add(worker);
                }
            }

            return result;
        }

        private static float RequestedWidthFor(OrcaExtensionDef def, OrcaExtensionRegistry registry, OrcaChatWindowContext context)
        {
            if (def == null || registry == null)
            {
                return 0f;
            }

            for (int i = 0; i < registry.chatWindowWidthHandlers.Count; i++)
            {
                OrcaExtensionHandler<Func<OrcaChatWindowContext, float>> entry = registry.chatWindowWidthHandlers[i];
                if (entry.def != def)
                {
                    continue;
                }

                return Mathf.Clamp(Invoke(entry, "Chat window extension width", handler => handler(context), 0f), 0f, MaxSingleExtraWidth);
            }

            return 0f;
        }

        private static bool Invoke<T>(OrcaExtensionHandler<T> entry, string hookName, Action<T> action) where T : class
        {
            if (entry == null || entry.handler == null || action == null)
            {
                return false;
            }

            try
            {
                action(entry.handler);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[RimAgent] " + hookName + " failed (" + EntryLabel(entry) + "): " + ex.Message);
                return false;
            }
        }

        private static TResult Invoke<T, TResult>(OrcaExtensionHandler<T> entry, string hookName, Func<T, TResult> action, TResult fallback) where T : class
        {
            if (entry == null || entry.handler == null || action == null)
            {
                return fallback;
            }

            try
            {
                return action(entry.handler);
            }
            catch (Exception ex)
            {
                Log.Warning("[RimAgent] " + hookName + " failed (" + EntryLabel(entry) + "): " + ex.Message);
                return fallback;
            }
        }

        private static string EntryLabel<T>(OrcaExtensionHandler<T> entry) where T : class
        {
            if (entry == null)
            {
                return "unknown";
            }

            return entry.capability.NullOrEmpty() ? entry.Label : entry.Label + "/" + entry.capability;
        }

        private static string WorkerLabel(OrcaExtensionDef def)
        {
            return def == null || def.defName.NullOrEmpty() ? "unknown" : def.defName;
        }
    }
}
