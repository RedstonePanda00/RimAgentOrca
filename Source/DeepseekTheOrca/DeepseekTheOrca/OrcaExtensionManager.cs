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

            DeepseekTheOrcaMod.Settings.SetExtensionEnabled(def.defName, enabled, def.defaultEnabled);
        }

        public static void AppendSystemPrompt(StringBuilder builder)
        {
            if (builder == null)
            {
                return;
            }

            List<OrcaExtensionWorker> workers = EnabledWorkers();
            bool wroteHeader = false;
            for (int i = 0; i < workers.Count; i++)
            {
                OrcaExtensionWorker worker = workers[i];
                int before = builder.Length;
                try
                {
                    worker.AppendSystemPrompt(builder);
                }
                catch (Exception ex)
                {
                    Log.Warning("[Deepseek The Orca] Extension prompt hook failed (" + WorkerLabel(worker) + "): " + ex.Message);
                }

                if (!wroteHeader && builder.Length > before)
                {
                    builder.Insert(before, "Enabled extension modules:\nExtensions may add runtime behavior, UI, routing, or prompt instructions. Treat prompt instructions as lower priority than safety, game validity, persona, and direct player intent.\n");
                    wroteHeader = true;
                }
            }
        }

        public static string ControllerRoutingHint()
        {
            List<string> hints = new List<string>();
            List<OrcaExtensionWorker> workers = EnabledWorkers();
            for (int i = 0; i < workers.Count; i++)
            {
                OrcaExtensionWorker worker = workers[i];
                try
                {
                    string hint = worker.ControllerRoutingHint();
                    if (!hint.NullOrEmpty())
                    {
                        hints.Add(hint);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[Deepseek The Orca] Extension routing hook failed (" + WorkerLabel(worker) + "): " + ex.Message);
                }
            }

            return hints.Count == 0 ? "" : " Enabled extension modules may affect routing. Extensions: " + string.Join("; ", hints.ToArray());
        }

        public static List<OrcaAgentNodeSpec> AgentNodes()
        {
            List<OrcaAgentNodeSpec> result = new List<OrcaAgentNodeSpec>();
            List<OrcaExtensionWorker> workers = EnabledWorkers();
            for (int i = 0; i < workers.Count; i++)
            {
                OrcaExtensionWorker worker = workers[i];
                try
                {
                    IEnumerable<OrcaAgentNodeSpec> specs = worker.AgentNodes();
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
                catch (Exception ex)
                {
                    Log.Warning("[Deepseek The Orca] Agent node extension hook failed (" + WorkerLabel(worker) + "): " + ex.Message);
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

            List<OrcaExtensionWorker> workers = EnabledWorkers();
            for (int i = 0; i < workers.Count; i++)
            {
                OrcaExtensionWorker worker = workers[i];
                try
                {
                    worker.OnAgentPhase(context);
                }
                catch (Exception ex)
                {
                    Log.Warning("[Deepseek The Orca] Agent phase extension hook failed (" + WorkerLabel(worker) + "): " + ex.Message);
                }
            }
        }

        public static void ModifyAgentRouting(OrcaAgentRoutingContext context)
        {
            if (context == null)
            {
                return;
            }

            List<OrcaExtensionWorker> workers = EnabledWorkers();
            for (int i = 0; i < workers.Count; i++)
            {
                OrcaExtensionWorker worker = workers[i];
                try
                {
                    worker.ModifyAgentRouting(context);
                }
                catch (Exception ex)
                {
                    Log.Warning("[Deepseek The Orca] Agent routing extension hook failed (" + WorkerLabel(worker) + "): " + ex.Message);
                }
            }
        }

        public static float RequestedExtraWidth(OrcaChatWindowContext context)
        {
            float width = 0f;
            List<OrcaExtensionWorker> workers = EnabledWorkers();
            for (int i = 0; i < workers.Count; i++)
            {
                OrcaExtensionWorker worker = workers[i];
                width += RequestedWidth(worker, context);
            }

            return Mathf.Clamp(width, 0f, MaxTotalExtraWidth);
        }

        public static void DrawRightExtensions(Rect rect, OrcaChatWindowContext context)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            List<OrcaExtensionWorker> workers = EnabledWorkers();
            float x = rect.x;
            for (int i = 0; i < workers.Count; i++)
            {
                OrcaExtensionWorker worker = workers[i];
                float width = Mathf.Min(RequestedWidth(worker, context), rect.xMax - x);
                if (width <= 0f)
                {
                    continue;
                }

                Rect workerRect = new Rect(x, rect.y, width, rect.height);
                try
                {
                    OrcaChatWindowContext workerContext = new OrcaChatWindowContext(
                        context == null ? null : context.session,
                        context == null ? Rect.zero : context.windowRect,
                        context == null ? Rect.zero : context.chatRect,
                        workerRect,
                        context == null ? 1f : context.alpha);
                    worker.DrawChatWindow(workerRect, workerContext);
                }
                catch (Exception ex)
                {
                    Log.Warning("[Deepseek The Orca] Chat window extension draw failed (" + WorkerLabel(worker) + "): " + ex.Message);
                }

                x += width;
                if (x >= rect.xMax)
                {
                    break;
                }
            }
        }

        public static void DrawOverlays(Rect windowRect, OrcaChatWindowContext context)
        {
            List<OrcaExtensionWorker> workers = EnabledWorkers();
            for (int i = 0; i < workers.Count; i++)
            {
                OrcaExtensionWorker worker = workers[i];
                try
                {
                    worker.DrawChatWindowOverlay(windowRect, context);
                }
                catch (Exception ex)
                {
                    Log.Warning("[Deepseek The Orca] Chat window extension overlay failed (" + WorkerLabel(worker) + "): " + ex.Message);
                }
            }
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

        private static float RequestedWidth(OrcaExtensionWorker worker, OrcaChatWindowContext context)
        {
            if (worker == null)
            {
                return 0f;
            }

            try
            {
                return Mathf.Clamp(worker.GetChatWindowExtraWidth(context), 0f, MaxSingleExtraWidth);
            }
            catch (Exception ex)
            {
                Log.Warning("[Deepseek The Orca] Chat window extension width failed (" + WorkerLabel(worker) + "): " + ex.Message);
                return 0f;
            }
        }

        private static string WorkerLabel(OrcaExtensionWorker worker)
        {
            return worker == null || worker.def == null ? "unknown" : worker.def.defName;
        }
    }
}
