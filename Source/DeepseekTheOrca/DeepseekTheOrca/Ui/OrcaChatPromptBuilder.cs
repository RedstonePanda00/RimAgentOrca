using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace DeepseekTheOrca
{
    public static class OrcaChatPromptBuilder
    {
        public static string BuildPlayerMessage(OrcaChatTurnContext context, IEnumerable<string> selectedSkillIds)
        {
            StringBuilder builder = new StringBuilder();
            OrcaExtensionManager.AppendUserMessageContext(builder, context);
            string playerName = context == null ? "" : context.playerName;
            string userText = context == null ? "" : context.text;
            builder.AppendLine("Player SteamPersonaName: " + playerName);
            AppendContextTags(builder, context == null ? null : context.contextTags);
            AppendSelectedSkillContext(builder, selectedSkillIds, userText);
            AppendKnowledgeContext(builder, userText);
            AppendMemoryContext(builder, userText);
            builder.AppendLine("Player message:");
            builder.Append(userText);
            return builder.ToString();
        }

        public static string BuildProactiveMessage(OrcaProactiveConversationRequest request, OrcaChatTurnContext context)
        {
            StringBuilder builder = new StringBuilder();
            OrcaExtensionManager.AppendUserMessageContext(builder, context);
            builder.AppendLine("Current game language: " + OrcaLanguageUtility.CurrentGameLanguage());
            builder.AppendLine("System proactive trigger source: " + request.source);
            builder.AppendLine("Trigger title: " + request.title);
            List<string> contextTags = context == null ? null : context.contextTags;
            AppendContextTags(builder, contextTags);
            AppendActiveSkillContext(builder, request.source + "\n" + request.title + "\n" + request.body, contextTags);
            string query = request.source + "\n" + request.title + "\n" + request.body;
            AppendKnowledgeContext(builder, query);
            AppendMemoryContext(builder, query);
            builder.AppendLine("Trigger details:");
            builder.AppendLine(request.body);
            builder.Append("This is not a player request. Speak proactively to the player in character. Reply in the current game language, even if the trigger details use English field labels. Do not call event execution tools for this trigger; the event has already been scheduled or observed.");
            return builder.ToString();
        }

        public static List<string> PlayerContextTags(string userText)
        {
            return new List<string> { "player_chat" };
        }

        public static List<string> ProactiveContextTags(OrcaProactiveConversationRequest request)
        {
            List<string> tags = new List<string> { "proactive" };
            if (request == null)
            {
                return tags;
            }

            string source = NormalizeContextTag(request.source);
            if (!source.NullOrEmpty())
            {
                tags.Add(source);
                tags.Add("proactive_" + source);
            }
            if (source == "storyteller_incident")
            {
                tags.Add("storyteller_action");
            }
            if (source == "colony_observation")
            {
                tags.Add("colony_state");
                tags.Add("recent_letter");
            }
            if (source == "rimtalk_chat_history")
            {
                tags.Add("rimtalk_context");
            }

            return tags.Distinct().ToList();
        }

        public static string BuildSystemPrompt()
        {
            StringBuilder builder = new StringBuilder();
            string personaPrompt = CurrentPersonaPrompt();
            if (!personaPrompt.NullOrEmpty())
            {
                builder.AppendLine(personaPrompt.Trim());
                builder.AppendLine();
            }

            string pluginPrompt = DeepseekTheOrcaMod.FormatEnabledPluginPrompt();
            if (!pluginPrompt.NullOrEmpty())
            {
                builder.AppendLine(pluginPrompt);
                builder.AppendLine();
            }

            builder.AppendLine("Common chat runtime rules:");
            builder.AppendLine("Knowledge base and long-term memory context may be included in user messages. Knowledge explains terms and lore. Long-term memory contains fuzzy remembered impressions across personas and colonies, weighted toward the current persona and save. Treat both as soft context below current game data from tools.");
            builder.AppendLine("Never mention hidden rolls, willingness chance, percentages, dice rolls, random rolls, validation, tool calls, JSON, internal state, or tool result internals to the player.");
            builder.AppendLine("You may inspect game data through tools when it would help you answer naturally: colony summary, recent letters, map pawns, pawn details, available incidents, and RimTalk chat history if available.");
            builder.AppendLine("If web search is available, you may use it for current external information outside the game. Do not use web search for current RimWorld colony state; use game tools for that. Treat web results as imperfect and summarize them naturally.");
            builder.AppendLine("If external MCP tools are available, they were configured by the player. Use them only when they directly help with the player's request, and treat their results as external tool output rather than RimWorld game state.");
            builder.AppendLine("RimTalk history may be read without explicit permission when it helps you understand colony conversation, player behavior, pawn relationships, or a proactive trigger. Its playerName is the value of RimTalk's player address/name configuration; do not treat it as the player's real name or SteamPersonaName. It only indicates how RimTalk was configured to refer to the player in that mod's dialogue context. Origin distinguishes player_initiated from ai_auto_generated dialogue.");
            builder.AppendLine("If a user message says it is a system proactive trigger, it is not from the player. Speak proactively about that trigger. For RimTalk proactive triggers, you may read RimTalk history before replying. Do not call execution tools for proactive triggers because the event was already scheduled or observed.");
            builder.AppendLine("The reply field is player-visible natural language only. Do not include XML-like tags, HTML-like tags, hidden channels, or control markup in reply.");
            builder.AppendLine("Respond in the same language the player uses unless asked otherwise. For proactive triggers, use the current game/player language rather than English trigger labels.");
            builder.AppendLine("Output exactly one JSON object and no extra text. JSON schema: " + ChatReplyJsonSchema() + ".");
            return builder.ToString();
        }

        public static string ChatReplyJsonSchema()
        {
            return OrcaExtensionManager.ChatReplyJsonSchema();
        }

        public static string CurrentPersonaSpeakerName()
        {
            string defName = DeepseekTheOrcaMod.Settings == null ? OrcaChatPersonaManager.BuiltInOrcaId : DeepseekTheOrcaMod.Settings.chatPersonaDefName;
            OrcaChatPersonaProfile persona = OrcaChatPersonaManager.Get(defName);
            if (persona == null || persona.label.NullOrEmpty())
            {
                return "DTO_OrcaChatSpeakerOrca".Translate().ToString();
            }

            return persona.label;
        }

        private static void AppendActiveSkillContext(StringBuilder builder, string turnText, List<string> contextTags)
        {
            string skillPrompt = OrcaSkillManager.FormatActiveSkillPrompt(turnText, contextTags);
            if (skillPrompt.NullOrEmpty())
            {
                return;
            }

            builder.AppendLine("Skill harness:");
            builder.AppendLine(skillPrompt);
        }

        private static void AppendSelectedSkillContext(StringBuilder builder, IEnumerable<string> selectedSkillIds, string turnText)
        {
            string skillPrompt = OrcaSkillManager.FormatSelectedSkillPrompt(selectedSkillIds, turnText);
            if (skillPrompt.NullOrEmpty())
            {
                return;
            }

            builder.AppendLine("Skill harness:");
            builder.AppendLine(skillPrompt);
        }

        private static void AppendContextTags(StringBuilder builder, List<string> contextTags)
        {
            if (builder == null || contextTags == null || contextTags.Count == 0)
            {
                return;
            }

            builder.AppendLine("Current turn context tags: " + string.Join(", ", contextTags.ToArray()));
        }

        private static string NormalizeContextTag(string value)
        {
            value = value == null ? "" : value.Trim().ToLowerInvariant();
            if (value.NullOrEmpty())
            {
                return "";
            }

            StringBuilder builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.' || c == ':')
                {
                    builder.Append(c);
                }
                else
                {
                    builder.Append('_');
                }
            }

            return builder.ToString().Trim('_');
        }

        private static void AppendKnowledgeContext(StringBuilder builder, string query)
        {
            string knowledgeContext = OrcaKnowledgeManager.ContextForPrompt(query);
            if (knowledgeContext.NullOrEmpty())
            {
                return;
            }

            builder.AppendLine("Knowledge context:");
            builder.AppendLine(knowledgeContext);
        }

        private static void AppendMemoryContext(StringBuilder builder, string query)
        {
            string memoryContext = OrcaSessionMemory.ContextForPrompt(query);
            if (memoryContext.NullOrEmpty())
            {
                return;
            }

            builder.AppendLine("Memory context:");
            builder.AppendLine(memoryContext);
        }

        private static string CurrentPersonaPrompt()
        {
            string defName = DeepseekTheOrcaMod.Settings == null ? OrcaChatPersonaManager.BuiltInOrcaId : DeepseekTheOrcaMod.Settings.chatPersonaDefName;
            OrcaChatPersonaProfile persona = OrcaChatPersonaManager.Get(defName);
            if (persona == null)
            {
                persona = OrcaChatPersonaManager.Get(OrcaChatPersonaManager.BuiltInOrcaId);
            }

            return persona == null ? "" : persona.prompt;
        }
    }
}
