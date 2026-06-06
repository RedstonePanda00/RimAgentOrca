using System.Collections.Generic;

namespace DeepseekTheOrca.Rimtalk
{
    public sealed class GetRimtalkChatHistoryTool : OrcaToolWorker
    {
        public string Name
        {
            get { return "get_rimtalk_chat_history"; }
        }

        public string Description
        {
            get { return "Read recent RimTalk chat records from RimTalk ApiHistory, with filters for origin, channel, state, pawn, and conversation id."; }
        }

        public override bool ShouldRegister()
        {
            return RimtalkIntegration.IsAvailable;
        }

        public override AiToolResult Invoke(AiToolContext context, Dictionary<string, string> arguments)
        {
            return RimtalkIntegration.GetChatHistory(arguments);
        }
    }
}

