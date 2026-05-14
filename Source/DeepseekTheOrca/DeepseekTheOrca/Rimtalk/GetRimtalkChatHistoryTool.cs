using System.Collections.Generic;

namespace DeepseekTheOrca.Rimtalk
{
    public sealed class GetRimtalkChatHistoryTool : IAiStoryTool
    {
        public string Name
        {
            get { return "get_rimtalk_chat_history"; }
        }

        public string Description
        {
            get { return "Read recent RimTalk chat records, distinguishing player-initiated dialogue from AI auto-generated dialogue."; }
        }

        public AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            return RimtalkIntegration.GetChatHistory(arguments);
        }
    }
}
