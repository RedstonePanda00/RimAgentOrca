using System;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaLocalRouteGate
    {
        public static OrcaLocalRouteDecision Decide(string text, DeepseekTheOrcaSettings settings, bool allowExecutionTools)
        {
            string lower = (text ?? "").ToLowerInvariant();
            OrcaLocalRouteDecision decision = new OrcaLocalRouteDecision();

            if (settings == null)
            {
                decision.route = "dialogue";
                decision.role = OrcaLlmModelRole.Dialogue;
                decision.reason = "settings unavailable";
                return decision;
            }

            if (LooksLikeWeb(lower) && settings.HasModelForRole(OrcaLlmModelRole.WebSearch) && settings.UsesLocalWebSearchTool)
            {
                return Direct("web_search", OrcaLlmModelRole.WebSearch, "local web-search intent");
            }

            if (LooksLikeVision(lower) && settings.HasModelForRole(OrcaLlmModelRole.Vision))
            {
                return Direct("vision", OrcaLlmModelRole.Vision, "local vision intent");
            }

            if (LooksLikeExecution(lower))
            {
                if (allowExecutionTools && settings.HasModelForRole(OrcaLlmModelRole.Tool))
                {
                    return Direct("tool", OrcaLlmModelRole.Tool, "local execution/tool intent");
                }

                return Direct("dialogue", OrcaLlmModelRole.Dialogue, "execution intent without tool permission");
            }

            if (LooksLikeGameState(lower) && settings.HasModelForRole(OrcaLlmModelRole.Tool))
            {
                return Direct("tool", OrcaLlmModelRole.Tool, "local game-state intent");
            }

            if (LooksComplexOrAmbiguous(lower) && settings.HasModelForRole(OrcaLlmModelRole.Controller))
            {
                decision.useController = true;
                decision.route = "controller";
                decision.role = OrcaLlmModelRole.Controller;
                decision.reason = "local route confidence low";
                return decision;
            }

            return Direct("dialogue", settings.HasModelForRole(OrcaLlmModelRole.Dialogue) ? OrcaLlmModelRole.Dialogue : OrcaChatRoleUtility.FirstAvailableChatModelRole(settings), "local dialogue intent");
        }

        private static OrcaLocalRouteDecision Direct(string route, OrcaLlmModelRole role, string reason)
        {
            return new OrcaLocalRouteDecision
            {
                route = route,
                role = role,
                reason = reason,
                useController = false
            };
        }

        private static bool LooksLikeWeb(string lower)
        {
            return ContainsAny(lower, "联网", "搜索", "网上", "新闻", "公开网页", "web search", "internet", "news");
        }

        private static bool LooksLikeVision(string lower)
        {
            return ContainsAny(lower, "图片", "截图", "图里", "看图", "识图", "image", "screenshot", "vision");
        }

        private static bool LooksLikeExecution(string lower)
        {
            return ContainsAny(lower, "触发", "执行", "召唤", "生成", "安排", "来一场", "袭击", "spawn", "trigger", "schedule", "execute", "raid now");
        }

        private static bool LooksLikeGameState(string lower)
        {
            return ContainsAny(lower,
                "殖民", "小人", "人物", "心情", "受伤", "健康", "关系", "恋人", "朋友", "敌人", "家人", "走到一起", "技能", "工作", "信件", "葬礼", "事件", "袭击", "rimtalk",
                "colonist", "pawn", "mood", "health", "injury", "relation", "relationship", "lover", "romance", "friend", "skill", "job", "letter", "incident", "raid", "funeral", "colony");
        }

        private static bool LooksComplexOrAmbiguous(string lower)
        {
            if (lower.Length > 180)
            {
                return true;
            }

            int hits = 0;
            if (LooksLikeWeb(lower)) hits++;
            if (LooksLikeVision(lower)) hits++;
            if (LooksLikeGameState(lower)) hits++;
            if (LooksLikeExecution(lower)) hits++;
            return hits >= 2;
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (text.Contains(needles[i]))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public sealed class OrcaLocalRouteDecision
    {
        public bool useController;
        public string route = "dialogue";
        public OrcaLlmModelRole role = OrcaLlmModelRole.Dialogue;
        public string reason = "";
    }
}
