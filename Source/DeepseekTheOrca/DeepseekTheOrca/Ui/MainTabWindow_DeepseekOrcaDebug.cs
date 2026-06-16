using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class MainTabWindow_DeepseekOrcaDebug : MainTabWindow
    {
        public override Vector2 RequestedTabSize
        {
            get { return new Vector2(980f, 520f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 40f, 32f), "DTO_OrcaChatTab".Translate());
            Text.Font = GameFont.Small;

            Rect debugButton = new Rect(inRect.width - 30f, 2f, 28f, 28f);
            TooltipHandler.TipRegion(debugButton, "Debug");
            if (Widgets.ButtonImage(debugButton, TexButton.OpenInspectSettings))
            {
                OrcaDebugWindowManager.Toggle();
            }

            float y = 48f;
            DrawOrcaChatControls(inRect, ref y);
        }

        private static void DrawOrcaChatControls(Rect inRect, ref float y)
        {
            DeepseekTheOrcaSettings settings = DeepseekTheOrcaMod.Settings;
            float alpha = settings == null ? 0.82f : Mathf.Clamp01(settings.chatWindowAlpha);

            Rect toggleRect = new Rect(0f, y, 220f, 32f);
            string label = OrcaChatWindowManager.IsOpen ? "DTO_OrcaChatClose".Translate() : "DTO_OrcaChatOpen".Translate();
            if (Widgets.ButtonText(toggleRect, label))
            {
                OrcaChatWindowManager.Toggle();
            }

            Widgets.Label(new Rect(toggleRect.xMax + 10f, y + 5f, inRect.width - toggleRect.width - 50f, 24f), "DTO_OrcaChatNote".Translate());
            y += 46f;

            OrcaChatWindowManager.Session.Tick();
            string status = OrcaChatWindowManager.Session.StatusText;
            if (status.NullOrEmpty())
            {
                status = "DTO_OrcaChatStatusIdle".Translate();
            }

            Widgets.Label(new Rect(0f, y, inRect.width, 24f), "DTO_OrcaChatStatus".Translate() + ": " + status);
            y += 34f;

            OrcaExtensionManager.DrawMainTabStatus(inRect, OrcaChatWindowManager.Session, ref y);

            Widgets.Label(new Rect(0f, y, 240f, 24f), "DTO_OrcaChatAlpha".Translate() + ": " + alpha.ToStringPercent());
            y += 28f;

            Rect sliderRect = new Rect(0f, y, Mathf.Min(560f, inRect.width), 24f);
            float newAlpha = Widgets.HorizontalSlider(sliderRect, alpha, 0f, 1f, roundTo: 0.01f);
            if (settings != null && Mathf.Abs(newAlpha - settings.chatWindowAlpha) > 0.001f)
            {
                settings.chatWindowAlpha = newAlpha;
                if (DeepseekTheOrcaMod.Instance != null)
                {
                    DeepseekTheOrcaMod.Instance.WriteSettings();
                }
            }
        }
    }

    public static class OrcaDebugWindowManager
    {
        public static bool IsOpen
        {
            get { return Find.WindowStack != null && Find.WindowStack.IsOpen(typeof(OrcaDebugWindow)); }
        }

        public static void Toggle()
        {
            if (Find.WindowStack == null)
            {
                return;
            }

            if (IsOpen)
            {
                Find.WindowStack.TryRemove(typeof(OrcaDebugWindow));
            }
            else
            {
                Find.WindowStack.Add(new OrcaDebugWindow());
            }
        }
    }

    public sealed class OrcaDebugWindow : Window
    {
        private enum DebugPage
        {
            CyclePlan,
            SingleTool,
            ChatLog
        }

        private DebugPage page;
        private Vector2 logScrollPosition;
        private Vector2 cyclePlanScrollPosition;
        private Vector2 toolSelectorScrollPosition;
        private Vector2 parameterScrollPosition;
        private Vector2 chatHistoryScrollPosition;
        private Vector2 chatDetailScrollPosition;
        private int selectedChatLogIndex = -1;
        private string selectedTool = "get_colony_summary";
        private string incidentDefBuffer = "ResourcePodCrash";
        private string pointsFactorBuffer = "1";
        private string reasonBuffer = "manual debug test";
        private string countBuffer = "5";
        private string filterBuffer = "all";
        private string pawnIdBuffer = "";
        private string factionDefBuffer = "";
        private string raidStrategyDefBuffer = "ImmediateAttack";
        private string raidArrivalModeDefBuffer = "";
        private string spawnCellBuffer = "";
        private string radiusBuffer = "5";
        private string queryBuffer = "";
        private readonly Dictionary<string, string> dynamicArgumentBuffers = new Dictionary<string, string>();

        private static readonly Color WindowBackground = new Color(0.035f, 0.04f, 0.05f, 0.94f);
        private static readonly Color PanelFill = new Color(0.05f, 0.055f, 0.065f, 0.72f);
        private static readonly Color BorderColor = new Color(0.42f, 0.45f, 0.48f, 1f);
        private static readonly Color MutedTextColor = new Color(0.65f, 0.67f, 0.7f, 1f);
        private static readonly Color AccentColor = new Color(0.75f, 0.78f, 0.86f, 1f);

        public OrcaDebugWindow()
        {
            doWindowBackground = false;
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            draggable = true;
            resizeable = true;
            drawShadow = true;
            onlyOneOfTypeAllowed = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(1040f, 640f); }
        }

        protected override float Margin
        {
            get { return 12f; }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, WindowBackground);
            DrawOutline(inRect);

            Rect content = inRect.ContractedBy(12f);
            float bottomHeight = 236f;
            float gap = 12f;
            Rect top = new Rect(content.x, content.y, content.width, content.height - bottomHeight - gap);
            Rect bottom = new Rect(content.x, top.yMax + gap, content.width, bottomHeight);

            float leftWidth = Mathf.Max(180f, top.width * 0.24f);
            Rect left = new Rect(top.x, top.y, leftWidth, top.height);
            Rect right = new Rect(left.xMax + gap, top.y, top.width - leftWidth - gap, top.height);

            DrawPanel(left);
            DrawPanel(right);
            DrawOptions(left.ContractedBy(10f));
            DrawSelectedPage(right.ContractedBy(10f));
            DrawBottom(bottom);

            OrcaChatWindowManager.Session.Tick();
        }

        private void DrawOptions(Rect rect)
        {
            Text.Font = GameFont.Small;
            float y = rect.y;
            DrawOptionButton(new Rect(rect.x, y, rect.width, 34f), "DTO_DebugCyclePlanTab".Translate(), DebugPage.CyclePlan);
            y += 42f;
            DrawOptionButton(new Rect(rect.x, y, rect.width, 34f), "DTO_DebugSingleToolTab".Translate(), DebugPage.SingleTool);
            y += 42f;
            DrawOptionButton(new Rect(rect.x, y, rect.width, 34f), "DTO_OrcaChatHistoryTab".Translate(), DebugPage.ChatLog);
        }

        private void DrawOptionButton(Rect rect, string label, DebugPage target)
        {
            bool selected = page == target;
            if (Widgets.ButtonText(rect, label, selected))
            {
                page = target;
                logScrollPosition = Vector2.zero;
                cyclePlanScrollPosition = Vector2.zero;
                parameterScrollPosition = Vector2.zero;
                toolSelectorScrollPosition = Vector2.zero;
                chatDetailScrollPosition = Vector2.zero;
            }
        }

        private void DrawSelectedPage(Rect rect)
        {
            if (page == DebugPage.CyclePlan)
            {
                DrawCyclePlanPage(rect);
            }
            else if (page == DebugPage.SingleTool)
            {
                DrawSingleToolPage(rect);
            }
            else
            {
                DrawOrcaChatHistory(rect);
            }
        }

        private void DrawCyclePlanPage(Rect rect)
        {
            DrawPanel(rect);
            Rect inner = rect.ContractedBy(8f);
            Widgets.LabelScrollable(inner, OrcaIncidentSchedule.DebugText(), ref cyclePlanScrollPosition, longLabel: true);
        }

        private void DrawSingleToolPage(Rect rect)
        {
            List<DebugToolSpec> specs = CurrentSingleToolSpecs();
            if (specs.Count == 0)
            {
                Widgets.Label(rect, "DTO_DebugNoTools".Translate());
                return;
            }

            DebugToolSpec spec = SelectedToolSpec(specs);
            float selectorWidth = 210f;
            Rect selector = new Rect(rect.x, rect.y, selectorWidth, rect.height);
            Rect detail = new Rect(selector.xMax + 12f, rect.y, rect.width - selectorWidth - 12f, rect.height);

            float selectorY = selector.y;
            Widgets.Label(new Rect(selector.x, selectorY, selector.width, 24f), "DTO_DebugSelectTool".Translate());
            selectorY += 28f;
            Rect selectorList = new Rect(selector.x, selectorY, selector.width, selector.yMax - selectorY);
            float rowHeight = 32f;
            Rect selectorView = new Rect(0f, 0f, selectorList.width - 16f, specs.Count * rowHeight);
            Widgets.BeginScrollView(selectorList, ref toolSelectorScrollPosition, selectorView);
            for (int i = 0; i < specs.Count; i++)
            {
                string toolName = specs[i].Name;
                Rect row = new Rect(0f, i * rowHeight, selectorView.width, 28f);
                if (Widgets.ButtonText(row, toolName, selectedTool == toolName))
                {
                    selectedTool = toolName;
                    parameterScrollPosition = Vector2.zero;
                }
            }
            Widgets.EndScrollView();

            float y = detail.y;
            Widgets.Label(new Rect(detail.x, y, detail.width, 24f), selectedTool);
            y += 30f;

            Rect parameterRect = new Rect(detail.x, y, detail.width, Mathf.Min(188f, detail.height * 0.42f));
            DrawSingleToolParameters(parameterRect, spec);
            y += parameterRect.height + 10f;

            Rect runRect = new Rect(detail.x, y, 180f, 32f);
            if (Widgets.ButtonText(runRect, "DTO_DebugRunSingleTool".Translate()))
            {
                SingleToolDebugRunner.Run(selectedTool, BuildSingleToolArguments(spec));
            }

            Rect clearRect = new Rect(runRect.xMax + 10f, y, 140f, 32f);
            if (Widgets.ButtonText(clearRect, "DTO_DebugClearLog".Translate()))
            {
                SingleToolDebugRunner.ClearLog();
            }
            y += 42f;

            Rect logRect = new Rect(detail.x, y, detail.width, detail.yMax - y);
            DrawScrollableLog(logRect, SingleToolDebugRunner.LogLines);
        }

        private void DrawOrcaChatHistory(Rect rect)
        {
            List<OrcaChatTurnLog> logs = OrcaChatWindowManager.Session.TurnLogs;
            if (logs.Count == 0)
            {
                Widgets.Label(rect, "DTO_OrcaChatLogEmpty".Translate());
                return;
            }

            if (selectedChatLogIndex < 0 || selectedChatLogIndex >= logs.Count)
            {
                selectedChatLogIndex = logs.Count - 1;
            }

            float listWidth = Mathf.Min(260f, rect.width * 0.36f);
            Rect listOuter = new Rect(rect.x, rect.y, listWidth, rect.height);
            Rect detailRect = new Rect(listOuter.xMax + 14f, rect.y, rect.width - listWidth - 14f, rect.height);
            DrawOrcaChatHistoryList(listOuter, logs);

            string logText = BuildOrcaChatTurnLog(logs[selectedChatLogIndex]);
            Widgets.LabelScrollable(detailRect, logText, ref chatDetailScrollPosition, longLabel: true);
        }

        private void DrawOrcaChatHistoryList(Rect outRect, List<OrcaChatTurnLog> logs)
        {
            float rowHeight = 30f;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, logs.Count * rowHeight);
            Widgets.BeginScrollView(outRect, ref chatHistoryScrollPosition, viewRect);
            for (int i = 0; i < logs.Count; i++)
            {
                Rect row = new Rect(0f, i * rowHeight, viewRect.width, rowHeight - 2f);
                if (Widgets.ButtonText(row, logs[i].Label, selectedChatLogIndex == i))
                {
                    selectedChatLogIndex = i;
                    chatDetailScrollPosition = Vector2.zero;
                }
            }
            Widgets.EndScrollView();
        }

        private static string BuildOrcaChatTurnLog(OrcaChatTurnLog turnLog)
        {
            StringBuilder builder = new StringBuilder();
            AppendLogSection(builder, "DTO_OrcaChatLogUser".Translate(), turnLog.UserText);
            AppendLogSection(builder, "DTO_OrcaChatLogProcess".Translate(), turnLog.ProcessText);
            AppendLogSection(builder, "DTO_OrcaChatLogReply".Translate(), turnLog.ReplyText);
            if (!turnLog.ErrorText.NullOrEmpty())
            {
                AppendLogSection(builder, "DTO_OrcaChatLogError".Translate(), turnLog.ErrorText);
            }

            return builder.ToString();
        }

        private static void AppendLogSection(StringBuilder builder, string title, string body)
        {
            if (body.NullOrEmpty())
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.AppendLine(title);
            builder.Append(body);
        }

        private void DrawSingleToolParameters(Rect outRect, DebugToolSpec spec)
        {
            DrawPanel(outRect);
            Rect inner = outRect.ContractedBy(8f);
            Rect viewRect = new Rect(0f, 0f, inner.width - 16f, SingleToolParameterViewHeight(spec));
            Widgets.BeginScrollView(inner, ref parameterScrollPosition, viewRect);

            float curY = 0f;
            if (spec.ArgumentNames.Length == 0)
            {
                Widgets.Label(new Rect(0f, curY, viewRect.width, 24f), "DTO_DebugNoArguments".Translate());
            }
            else
            {
                for (int i = 0; i < spec.ArgumentNames.Length; i++)
                {
                    string argumentName = spec.ArgumentNames[i];
                    DrawSingleToolArgumentField(argumentName, viewRect.width, ref curY);
                    curY += 8f;
                }
            }

            Widgets.EndScrollView();
        }

        private void DrawSingleToolArgumentField(string argumentName, float width, ref float y)
        {
            Widgets.Label(new Rect(0f, y, width, 24f), argumentName);
            y += 24f;

            if (argumentName == "reason")
            {
                reasonBuffer = Widgets.TextArea(new Rect(0f, y, width, 70f), reasonBuffer);
                y += 70f;
                return;
            }

            if (argumentName == "incidentDef")
            {
                incidentDefBuffer = Widgets.TextField(new Rect(0f, y, width, 28f), incidentDefBuffer);
            }
            else if (argumentName == "pointsFactor")
            {
                pointsFactorBuffer = Widgets.TextField(new Rect(0f, y, width, 28f), pointsFactorBuffer);
            }
            else if (argumentName == "count")
            {
                countBuffer = Widgets.TextField(new Rect(0f, y, width, 28f), countBuffer);
            }
            else if (argumentName == "filter")
            {
                filterBuffer = Widgets.TextField(new Rect(0f, y, width, 28f), filterBuffer);
            }
            else if (argumentName == "pawnId")
            {
                pawnIdBuffer = Widgets.TextField(new Rect(0f, y, width, 28f), pawnIdBuffer);
            }
            else if (argumentName == "factionDef")
            {
                factionDefBuffer = Widgets.TextField(new Rect(0f, y, width, 28f), factionDefBuffer);
            }
            else if (argumentName == "raidStrategyDef")
            {
                raidStrategyDefBuffer = Widgets.TextField(new Rect(0f, y, width, 28f), raidStrategyDefBuffer);
            }
            else if (argumentName == "raidArrivalModeDef")
            {
                raidArrivalModeDefBuffer = Widgets.TextField(new Rect(0f, y, width, 28f), raidArrivalModeDefBuffer);
            }
            else if (argumentName == "spawnCell")
            {
                spawnCellBuffer = Widgets.TextField(new Rect(0f, y, width, 28f), spawnCellBuffer);
            }
            else if (argumentName == "radius")
            {
                radiusBuffer = Widgets.TextField(new Rect(0f, y, width, 28f), radiusBuffer);
            }
            else if (argumentName == "query")
            {
                queryBuffer = Widgets.TextField(new Rect(0f, y, width, 28f), queryBuffer);
            }
            else
            {
                string buffer = GenericArgumentBuffer(argumentName);
                buffer = Widgets.TextField(new Rect(0f, y, width, 28f), buffer);
                dynamicArgumentBuffers[argumentName] = buffer;
            }

            y += 28f;
        }

        private static float SingleToolParameterViewHeight(DebugToolSpec spec)
        {
            if (spec.ArgumentNames.Length == 0)
            {
                return 32f;
            }

            float height = 0f;
            for (int i = 0; i < spec.ArgumentNames.Length; i++)
            {
                height += spec.ArgumentNames[i] == "reason" ? 102f : 60f;
            }

            return Mathf.Max(height, 32f);
        }

        private Dictionary<string, string> BuildSingleToolArguments(DebugToolSpec spec)
        {
            Dictionary<string, string> arguments = new Dictionary<string, string>();
            for (int i = 0; i < spec.ArgumentNames.Length; i++)
            {
                string argumentName = spec.ArgumentNames[i];
                if (argumentName == "incidentDef" && !incidentDefBuffer.NullOrEmpty())
                {
                    arguments["incidentDef"] = incidentDefBuffer;
                }
                else if (argumentName == "pointsFactor" && !pointsFactorBuffer.NullOrEmpty())
                {
                    arguments["pointsFactor"] = pointsFactorBuffer;
                }
                else if (argumentName == "reason" && !reasonBuffer.NullOrEmpty())
                {
                    arguments["reason"] = reasonBuffer;
                }
                else if (argumentName == "count" && spec.Name == "web_search" && !countBuffer.NullOrEmpty())
                {
                    arguments["maxResults"] = countBuffer;
                }
                else if (argumentName == "count" && !countBuffer.NullOrEmpty())
                {
                    arguments["count"] = countBuffer;
                }
                else if (argumentName == "filter" && !filterBuffer.NullOrEmpty())
                {
                    arguments["filter"] = filterBuffer;
                }
                else if (argumentName == "pawnId" && !pawnIdBuffer.NullOrEmpty())
                {
                    arguments["pawnId"] = pawnIdBuffer;
                }
                else if (argumentName == "factionDef" && !factionDefBuffer.NullOrEmpty())
                {
                    arguments["factionDef"] = factionDefBuffer;
                }
                else if (argumentName == "raidStrategyDef" && !raidStrategyDefBuffer.NullOrEmpty())
                {
                    arguments["raidStrategyDef"] = raidStrategyDefBuffer;
                }
                else if (argumentName == "raidArrivalModeDef" && !raidArrivalModeDefBuffer.NullOrEmpty())
                {
                    arguments["raidArrivalModeDef"] = raidArrivalModeDefBuffer;
                }
                else if (argumentName == "spawnCell" && !spawnCellBuffer.NullOrEmpty())
                {
                    arguments["spawnCell"] = spawnCellBuffer;
                }
                else if (argumentName == "radius" && !radiusBuffer.NullOrEmpty())
                {
                    arguments["radius"] = radiusBuffer;
                }
                else if (argumentName == "query" && !queryBuffer.NullOrEmpty())
                {
                    arguments["query"] = queryBuffer;
                }
                else
                {
                    string value = GenericArgumentBuffer(argumentName);
                    if (!value.NullOrEmpty())
                    {
                        arguments[argumentName] = value;
                    }
                }
            }

            return arguments;
        }

        private string GenericArgumentBuffer(string argumentName)
        {
            string value;
            if (!dynamicArgumentBuffers.TryGetValue(argumentName, out value))
            {
                value = "";
                dynamicArgumentBuffers[argumentName] = value;
            }

            return value;
        }

        private static List<DebugToolSpec> CurrentSingleToolSpecs()
        {
            List<DebugToolSpec> specs = new List<DebugToolSpec>();
            foreach (AiToolDefinition definition in AiStoryToolRegistry.AllDefinitions)
            {
                if (definition == null || definition.Name.NullOrEmpty())
                {
                    continue;
                }

                specs.Add(DebugToolSpec.FromDefinition(definition));
            }

            return specs.OrderBy(spec => spec.Name).ToList();
        }

        private DebugToolSpec SelectedToolSpec(List<DebugToolSpec> specs)
        {
            for (int i = 0; i < specs.Count; i++)
            {
                if (specs[i].Name == selectedTool)
                {
                    return specs[i];
                }
            }

            selectedTool = specs[0].Name;
            return specs[0];
        }

        private void DrawScrollableLog(Rect rect, IEnumerable<string> lines)
        {
            DrawPanel(rect);
            Rect inner = rect.ContractedBy(8f);
            string logText = string.Join("\n", lines.ToArray());
            if (logText.NullOrEmpty())
            {
                logText = "DTO_DebugLogEmpty".Translate();
            }

            Widgets.LabelScrollable(inner, logText, ref logScrollPosition, longLabel: true);
        }

        private void DrawBottom(Rect rect)
        {
            float gap = 12f;
            float chartWidth = Mathf.Min(380f, Mathf.Max(300f, rect.width * 0.38f));
            float actionWidth = 150f;
            Rect chart = new Rect(rect.x, rect.y, chartWidth, rect.height);
            Rect actions = new Rect(rect.xMax - actionWidth, rect.y, actionWidth, rect.height);
            Rect status = new Rect(chart.xMax + gap, rect.y, actions.x - chart.xMax - gap * 2f, rect.height);

            DrawPanel(chart);
            DrawUsageChart(chart.ContractedBy(10f));
            DrawPanel(status);
            DrawStatusPanel(status.ContractedBy(10f));
            DrawPanel(actions);
            DrawActionPanel(actions.ContractedBy(10f));
        }

        private static void DrawUsageChart(Rect rect)
        {
            List<LlmUsageSample> samples = LlmUsageTracker.Snapshot();
            Text.Font = GameFont.Tiny;
            GUI.color = MutedTextColor;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 20f), "Tokens");
            GUI.color = Color.white;

            Rect graph = new Rect(rect.x, rect.y + 22f, rect.width, rect.height - 28f);
            Widgets.DrawLineHorizontal(graph.x, graph.yMax - 1f, graph.width, BorderColor);
            if (samples.Count == 0)
            {
                GUI.color = MutedTextColor;
                Widgets.Label(graph, "No token samples yet.");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                return;
            }

            int max = Mathf.Max(1, samples.Max(sample => sample.totalTokens));
            for (int i = 0; i < samples.Count; i++)
            {
                float x = samples.Count == 1 ? graph.x : Mathf.Lerp(graph.x, graph.xMax, (float)i / (samples.Count - 1));
                float y = Mathf.Lerp(graph.yMax - 2f, graph.y + 4f, Mathf.Clamp01((float)samples[i].totalTokens / max));
                Widgets.DrawBoxSolid(new Rect(x - 1f, y - 1f, 2f, 2f), AccentColor);
                if (i > 0)
                {
                    float prevX = samples.Count == 1 ? graph.x : Mathf.Lerp(graph.x, graph.xMax, (float)(i - 1) / (samples.Count - 1));
                    float prevY = Mathf.Lerp(graph.yMax - 2f, graph.y + 4f, Mathf.Clamp01((float)samples[i - 1].totalTokens / max));
                    Widgets.DrawLine(new Vector2(prevX, prevY), new Vector2(x, y), AccentColor, 1f);
                }
            }

            GUI.color = MutedTextColor;
            Widgets.Label(new Rect(graph.x, graph.y, 80f, 20f), max.ToString());
            Widgets.Label(new Rect(graph.x, graph.yMax - 20f, 80f, 20f), "0");
            Widgets.Label(new Rect(graph.xMax - 90f, graph.yMax - 20f, 90f, 20f), "now");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private static void DrawStatusPanel(Rect rect)
        {
            LlmConnectionStatus connectionStatus;
            string connectionMessage;
            LlmConnectionTester.Snapshot(out connectionStatus, out connectionMessage);

            LlmUsageSummary summary = LlmUsageTracker.Summary();
            OrcaChatSession session = OrcaChatWindowManager.Session;
            string mapText = Find.CurrentMap == null
                ? "DTO_DebugNoCurrentMap".Translate().ToString()
                : (Find.CurrentMap.Parent == null ? Find.CurrentMap.ToString() : Find.CurrentMap.Parent.LabelCap.ToString());

            float gap = 12f;
            float columnWidth = (rect.width - gap) / 2f;
            Rect left = new Rect(rect.x, rect.y, columnWidth, rect.height);
            Rect right = new Rect(left.xMax + gap, rect.y, columnWidth, rect.height);
            float leftY = left.y;
            float rightY = right.y;

            DrawStatusLine(left, ref leftY, "AI status", AiStatusText());
            DrawStatusLine(left, ref leftY, "Map", mapText);
            DrawStatusLine(left, ref leftY, "Connection", OrcaSettingsFormatters.ConnectionStatusText(connectionStatus));
            DrawStatusLine(left, ref leftY, "Route", session.LastControllerRoute);
            DrawStatusLine(left, ref leftY, "Model role", EmptyAsDash(session.CurrentModelRoleLabel));
            DrawStatusLine(left, ref leftY, "Model", TrimForPanel(session.CurrentModelReference, 38));
            DrawStatusLine(left, ref leftY, "Last error", TrimForPanel(EmptyAsDash(session.LastErrorText), 38));

            DrawStatusLine(right, ref rightY, "LLM calls", summary.totalCalls.ToString());
            DrawStatusLine(right, ref rightY, "By role", FormatRoleCalls(summary));
            DrawStatusLine(right, ref rightY, "Last LLM", FormatLastLlm(summary));
            DrawStatusLine(right, ref rightY, "Last tokens", summary.lastTokens.ToString());
            DrawStatusLine(right, ref rightY, "Total tokens", summary.totalTokens.ToString());
            DrawStatusLine(right, ref rightY, "Avg tokens", summary.averageTokens.ToString("0.0"));
            DrawStatusLine(right, ref rightY, "Avg ms", summary.averageElapsedMs.ToString("0"));
            DrawStatusLine(right, ref rightY, "Tool calls", session.TotalToolCalls + " / failed " + session.FailedToolCalls);
            DrawStatusLine(right, ref rightY, "Last tool", TrimForPanel(FormatLastTool(session), 34));
        }

        private static void DrawStatusLine(Rect rect, ref float y, string label, string value)
        {
            GUI.color = MutedTextColor;
            Widgets.Label(new Rect(rect.x, y, 82f, 22f), label + ":");
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 90f, y, rect.width - 90f, 22f), value);
            y += 22f;
        }

        private static string FormatRoleCalls(LlmUsageSummary summary)
        {
            string text = "C " + summary.controllerCalls
                + " Dlg " + summary.dialogueCalls
                + " Tool " + summary.toolModelCalls
                + " Dec " + summary.decisionCalls;
            if (summary.webSearchCalls > 0)
            {
                text += " Web " + summary.webSearchCalls;
            }
            if (summary.visionCalls > 0)
            {
                text += " Vis " + summary.visionCalls;
            }
            if (summary.fallbackCalls > 0)
            {
                text += " F " + summary.fallbackCalls;
            }

            return text;
        }

        private static string FormatLastLlm(LlmUsageSummary summary)
        {
            string role = EmptyAsDash(summary.lastRole);
            string model = TrimForPanel(summary.lastModel, 24);
            return model == "-" ? role : role + " / " + model;
        }

        private static string FormatLastTool(OrcaChatSession session)
        {
            if (session == null || session.LastToolName.NullOrEmpty())
            {
                return "-";
            }

            return session.LastToolName + " " + session.LastToolResult;
        }

        private static string EmptyAsDash(string value)
        {
            return value.NullOrEmpty() ? "-" : value;
        }

        private static string TrimForPanel(string value, int maxChars)
        {
            value = EmptyAsDash(value).Replace("\r", " ").Replace("\n", " ");
            if (value.Length <= maxChars)
            {
                return value;
            }

            return value.Substring(0, Mathf.Max(0, maxChars - 3)) + "...";
        }

        private static string AiStatusText()
        {
            if (OrcaChatWindowManager.Session.IsWaiting)
            {
                return "waiting";
            }

            return "idle";
        }

        private static void DrawActionPanel(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 28f), DeepseekTheOrcaMod.DisplayName);
            Rect settingsRect = new Rect(rect.x, rect.y + 42f, rect.width, 32f);
            if (Widgets.ButtonText(settingsRect, "Settings"))
            {
                if (DeepseekTheOrcaMod.Instance != null)
                {
                    Find.WindowStack.Add(new Dialog_ModSettings(DeepseekTheOrcaMod.Instance));
                }
            }
        }

        private static void DrawPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelFill);
            DrawOutline(rect);
        }

        private static void DrawOutline(Rect rect)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 1f), BorderColor);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), BorderColor);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 1f, rect.height), BorderColor);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), BorderColor);
        }

        private sealed class DebugToolSpec
        {
            public readonly string Name;
            public readonly string[] ArgumentNames;

            public DebugToolSpec(string name, string[] argumentNames)
            {
                Name = name;
                ArgumentNames = argumentNames;
            }

            public static DebugToolSpec FromDefinition(AiToolDefinition definition)
            {
                List<string> argumentNames = new List<string>();
                Dictionary<string, object> properties = null;
                object propertiesObj;
                if (definition.parameters != null && definition.parameters.TryGetValue("properties", out propertiesObj))
                {
                    properties = propertiesObj as Dictionary<string, object>;
                }

                if (properties != null)
                {
                    foreach (string key in properties.Keys)
                    {
                        if (!key.NullOrEmpty())
                        {
                            argumentNames.Add(key);
                        }
                    }
                }

                return new DebugToolSpec(definition.Name, argumentNames.ToArray());
            }
        }
    }
}
