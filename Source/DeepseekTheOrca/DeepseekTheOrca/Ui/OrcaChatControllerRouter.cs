using System.Collections.Generic;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaChatControllerRouter
    {
        public static List<LlmChatMessage> BuildControllerMessages(List<LlmChatMessage> chatMessages, string lastUserText)
        {
            string latestUserContent = LatestUserContent(chatMessages);
            List<LlmChatMessage> controllerMessages = new List<LlmChatMessage>();
            string skillRoutingHint = OrcaSkillManager.FormatControllerRoutingHint();
            string skillSelectionCatalog = OrcaSkillManager.FormatSkillSelectionCatalog();
            string pluginRoutingHint = DeepseekTheOrcaMod.FormatPluginControllerRoutingHint();
            controllerMessages.Add(LlmChatMessage.System(
                "You are the chat controller model. Route the latest RimWorld chat turn to exactly one specialist. "
                + "Return exactly one JSON object and no extra text. "
                + "Schema: {\"route\":\"dialogue|tool|web_search|vision\",\"skillIds\":[\"enabled skill id\"],\"reason\":\"short reason\"}. "
                + "Use dialogue for ordinary conversation and final wording. "
                + "Use tool when current game state, pawns, incidents, RimTalk history, MCP tools, or event execution may be needed. "
                + "Use web_search only for current external public-web information outside the game. "
                + "Use vision only when the request clearly depends on image recognition. "
                + "Select skillIds only from the enabled skill catalog, based on each skill description and the latest player request. "
                + "Use an empty skillIds array when no skill is directly relevant. "
                + "If unsure, choose tool when game state might matter, otherwise dialogue. "
                + AppendControllerHint(skillRoutingHint)
                + AppendControllerHint(skillSelectionCatalog)
                + AppendControllerHint(pluginRoutingHint)));
            controllerMessages.Add(LlmChatMessage.User(lastUserText.NullOrEmpty() ? latestUserContent : lastUserText));
            return controllerMessages;
        }

        public static OrcaControllerDecision ParseDecision(string content)
        {
            OrcaControllerDecision decision = new OrcaControllerDecision();
            try
            {
                Dictionary<string, object> parsed = MiniJson.Deserialize(ExtractJsonObject(content ?? "")) as Dictionary<string, object>;
                string route = GetParsedString(parsed, "route");
                if (!route.NullOrEmpty())
                {
                    decision.route = route.Trim().ToLowerInvariant();
                }

                object skillIdsObj;
                List<object> skillIds = parsed != null && parsed.TryGetValue("skillIds", out skillIdsObj) ? skillIdsObj as List<object> : null;
                if (skillIds == null && parsed != null && parsed.TryGetValue("skill_ids", out skillIdsObj))
                {
                    skillIds = skillIdsObj as List<object>;
                }

                if (skillIds != null)
                {
                    for (int i = 0; i < skillIds.Count; i++)
                    {
                        string id = skillIds[i] == null ? "" : skillIds[i].ToString().Trim();
                        if (!id.NullOrEmpty() && !decision.skillIds.Contains(id))
                        {
                            decision.skillIds.Add(id);
                        }
                    }
                }
            }
            catch
            {
            }

            return decision;
        }

        public static OrcaLlmModelRole ModelRoleForRoute(string route, DeepseekTheOrcaSettings settings)
        {
            if (route == "web_search")
            {
                return settings != null && settings.UsesLocalWebSearchTool
                    ? OrcaLlmModelRole.WebSearch
                    : OrcaLlmModelRole.Dialogue;
            }

            if (route == "vision")
            {
                return OrcaLlmModelRole.Vision;
            }

            if (route == "tool")
            {
                return OrcaLlmModelRole.Tool;
            }

            return OrcaLlmModelRole.Dialogue;
        }

        private static string LatestUserContent(List<LlmChatMessage> chatMessages)
        {
            if (chatMessages == null)
            {
                return "";
            }

            for (int i = chatMessages.Count - 1; i >= 0; i--)
            {
                if (chatMessages[i].role == "user")
                {
                    return chatMessages[i].content ?? "";
                }
            }

            return "";
        }

        private static string ExtractJsonObject(string content)
        {
            int start = content.IndexOf('{');
            int end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return content.Substring(start, end - start + 1);
            }

            return content;
        }

        private static string GetParsedString(Dictionary<string, object> parsed, string key)
        {
            object value;
            if (parsed == null || !parsed.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            return value.ToString();
        }

        private static string AppendControllerHint(string hint)
        {
            if (hint.NullOrEmpty())
            {
                return "";
            }

            return "\n" + hint.Trim() + "\n";
        }
    }

    public sealed class OrcaControllerDecision
    {
        public string route = "dialogue";
        public readonly List<string> skillIds = new List<string>();
    }
}
