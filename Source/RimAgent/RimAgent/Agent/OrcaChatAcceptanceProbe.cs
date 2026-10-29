using System.Linq;
using Verse;

namespace RimAgent;

public sealed class OrcaChatAcceptanceProbe : OrcaExtensionWorker
{
    private const string Id = "DTO_IsolatedChatAcceptance";
    private static int starts, replies, completions;
    private static string source = "";
    private static bool proactive;

    internal static void Install()
    {
        var definition = DefDatabase<OrcaExtensionDef>.GetNamedSilentFail(Id);
        if (definition == null)
        {
            definition = new OrcaExtensionDef { defName = Id, label = "Isolated chat acceptance", workerClass = typeof(OrcaChatAcceptanceProbe) };
            DefDatabase<OrcaExtensionDef>.Add(definition);
        }
        OrcaExtensionManager.SetExtensionEnabled(definition, true);
        OrcaChatWindowManager.Session.Clear();
        starts = replies = completions = 0; source = ""; proactive = false;
    }

    public override void Register(OrcaExtensionRegistry registry)
    {
        registry.AddChatTurnStarting(context => { starts++; source = context.source; proactive = context.proactive; });
        registry.AddUserMessageContext((builder, _) => builder.AppendLine("[isolated-extension-context]"));
        registry.AddChatReply(_ => replies++);
        registry.AddAgentPhase(context => { if (context.phase == OrcaAgentPhase.Completed) completions++; });
    }

    internal static object Snapshot() => new { starts, replies, completions, source, proactive, session = OrcaChatWindowManager.Session.AcceptanceSnapshot() };
}

public sealed partial class OrcaChatSession
{
    internal object AcceptanceSnapshot() => new {
        userTurns = transcript.Messages.Count(message => message.role == "user"),
        displayLines = transcript.DisplayLines.Count,
        roles = transcript.Messages.Select(message => message.role).ToArray(),
        allowExecutionToolsThisTurn
    };
}
