using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace DeepseekTheOrca
{
    public sealed class OrcaExtensionHandler<T> where T : class
    {
        public readonly OrcaExtensionDef def;
        public readonly string capability;
        public readonly T handler;

        public OrcaExtensionHandler(OrcaExtensionDef def, string capability, T handler)
        {
            this.def = def;
            this.capability = capability ?? "";
            this.handler = handler;
        }

        public string Label
        {
            get { return def == null || def.defName.NullOrEmpty() ? "unknown" : def.defName; }
        }
    }

    public sealed class OrcaExtensionRegistry
    {
        private readonly OrcaExtensionDef owner;

        internal readonly List<OrcaExtensionHandler<Action<StringBuilder>>> systemPromptHandlers = new List<OrcaExtensionHandler<Action<StringBuilder>>>();
        internal readonly List<OrcaExtensionHandler<Func<string>>> controllerRoutingHintHandlers = new List<OrcaExtensionHandler<Func<string>>>();
        internal readonly List<OrcaExtensionHandler<Action<OrcaChatTurnContext>>> chatTurnStartingHandlers = new List<OrcaExtensionHandler<Action<OrcaChatTurnContext>>>();
        internal readonly List<OrcaExtensionHandler<Action<StringBuilder, OrcaChatTurnContext>>> userMessageContextHandlers = new List<OrcaExtensionHandler<Action<StringBuilder, OrcaChatTurnContext>>>();
        internal readonly List<OrcaExtensionHandler<Action<Dictionary<string, object>>>> chatReplySchemaHandlers = new List<OrcaExtensionHandler<Action<Dictionary<string, object>>>>();
        internal readonly List<OrcaExtensionHandler<Action<OrcaChatReplyContext>>> chatReplyHandlers = new List<OrcaExtensionHandler<Action<OrcaChatReplyContext>>>();
        internal readonly List<OrcaExtensionHandler<Action<OrcaChatSession>>> chatSessionClearedHandlers = new List<OrcaExtensionHandler<Action<OrcaChatSession>>>();
        internal readonly List<OrcaExtensionHandler<Action>> enabledHandlers = new List<OrcaExtensionHandler<Action>>();
        internal readonly List<OrcaExtensionHandler<Action>> disabledHandlers = new List<OrcaExtensionHandler<Action>>();
        internal readonly List<OrcaExtensionHandler<Func<IEnumerable<OrcaAgentNodeSpec>>>> agentNodeHandlers = new List<OrcaExtensionHandler<Func<IEnumerable<OrcaAgentNodeSpec>>>>();
        internal readonly List<OrcaExtensionHandler<Action<OrcaAgentPhaseContext>>> agentPhaseHandlers = new List<OrcaExtensionHandler<Action<OrcaAgentPhaseContext>>>();
        internal readonly List<OrcaExtensionHandler<Action<OrcaAgentRoutingContext>>> agentRoutingHandlers = new List<OrcaExtensionHandler<Action<OrcaAgentRoutingContext>>>();
        internal readonly List<OrcaExtensionHandler<Action<OrcaExecutionGateContext>>> executionGateHandlers = new List<OrcaExtensionHandler<Action<OrcaExecutionGateContext>>>();
        internal readonly List<OrcaExtensionHandler<Func<OrcaChatWindowContext, float>>> chatWindowWidthHandlers = new List<OrcaExtensionHandler<Func<OrcaChatWindowContext, float>>>();
        internal readonly List<OrcaExtensionHandler<Action<Rect, OrcaChatWindowContext>>> chatWindowDrawHandlers = new List<OrcaExtensionHandler<Action<Rect, OrcaChatWindowContext>>>();
        internal readonly List<OrcaExtensionHandler<Action<Rect, OrcaChatWindowContext>>> chatWindowOverlayHandlers = new List<OrcaExtensionHandler<Action<Rect, OrcaChatWindowContext>>>();
        internal readonly List<OrcaExtensionHandler<Action<OrcaMainTabStatusContext>>> mainTabStatusHandlers = new List<OrcaExtensionHandler<Action<OrcaMainTabStatusContext>>>();

        public OrcaExtensionRegistry()
        {
        }

        internal OrcaExtensionRegistry(OrcaExtensionDef owner)
        {
            this.owner = owner;
        }

        public void AddSystemPrompt(Action<StringBuilder> handler, string capability = "prompt")
        {
            Add(systemPromptHandlers, capability, handler);
        }

        public void AddControllerRoutingHint(Func<string> handler, string capability = "agent_routing")
        {
            Add(controllerRoutingHintHandlers, capability, handler);
        }

        public void AddChatTurnStarting(Action<OrcaChatTurnContext> handler, string capability = "chat_lifecycle")
        {
            Add(chatTurnStartingHandlers, capability, handler);
        }

        public void AddUserMessageContext(Action<StringBuilder, OrcaChatTurnContext> handler, string capability = "prompt_context")
        {
            Add(userMessageContextHandlers, capability, handler);
        }

        public void AddChatReplySchema(Action<Dictionary<string, object>> handler, string capability = "reply_schema")
        {
            Add(chatReplySchemaHandlers, capability, handler);
        }

        public void AddChatReply(Action<OrcaChatReplyContext> handler, string capability = "chat_lifecycle")
        {
            Add(chatReplyHandlers, capability, handler);
        }

        public void AddChatSessionCleared(Action<OrcaChatSession> handler, string capability = "chat_lifecycle")
        {
            Add(chatSessionClearedHandlers, capability, handler);
        }

        public void AddEnabled(Action handler, string capability = "lifecycle")
        {
            Add(enabledHandlers, capability, handler);
        }

        public void AddDisabled(Action handler, string capability = "lifecycle")
        {
            Add(disabledHandlers, capability, handler);
        }

        public void AddAgentNodes(Func<IEnumerable<OrcaAgentNodeSpec>> handler, string capability = "agent_node")
        {
            Add(agentNodeHandlers, capability, handler);
        }

        public void AddAgentPhase(Action<OrcaAgentPhaseContext> handler, string capability = "agent_lifecycle")
        {
            Add(agentPhaseHandlers, capability, handler);
        }

        public void AddAgentRouting(Action<OrcaAgentRoutingContext> handler, string capability = "agent_routing")
        {
            Add(agentRoutingHandlers, capability, handler);
        }

        public void AddExecutionGate(Action<OrcaExecutionGateContext> handler, string capability = "execution_gate")
        {
            Add(executionGateHandlers, capability, handler);
        }

        public void AddChatWindowWidth(Func<OrcaChatWindowContext, float> handler, string capability = "chat_window_ui")
        {
            Add(chatWindowWidthHandlers, capability, handler);
        }

        public void AddChatWindowDraw(Action<Rect, OrcaChatWindowContext> handler, string capability = "chat_window_ui")
        {
            Add(chatWindowDrawHandlers, capability, handler);
        }

        public void AddChatWindowOverlay(Action<Rect, OrcaChatWindowContext> handler, string capability = "chat_window_ui")
        {
            Add(chatWindowOverlayHandlers, capability, handler);
        }

        public void AddMainTabStatus(Action<OrcaMainTabStatusContext> handler, string capability = "main_tab_status")
        {
            Add(mainTabStatusHandlers, capability, handler);
        }

        internal void MergeFrom(OrcaExtensionRegistry other)
        {
            if (other == null)
            {
                return;
            }

            systemPromptHandlers.AddRange(other.systemPromptHandlers);
            controllerRoutingHintHandlers.AddRange(other.controllerRoutingHintHandlers);
            chatTurnStartingHandlers.AddRange(other.chatTurnStartingHandlers);
            userMessageContextHandlers.AddRange(other.userMessageContextHandlers);
            chatReplySchemaHandlers.AddRange(other.chatReplySchemaHandlers);
            chatReplyHandlers.AddRange(other.chatReplyHandlers);
            chatSessionClearedHandlers.AddRange(other.chatSessionClearedHandlers);
            enabledHandlers.AddRange(other.enabledHandlers);
            disabledHandlers.AddRange(other.disabledHandlers);
            agentNodeHandlers.AddRange(other.agentNodeHandlers);
            agentPhaseHandlers.AddRange(other.agentPhaseHandlers);
            agentRoutingHandlers.AddRange(other.agentRoutingHandlers);
            executionGateHandlers.AddRange(other.executionGateHandlers);
            chatWindowWidthHandlers.AddRange(other.chatWindowWidthHandlers);
            chatWindowDrawHandlers.AddRange(other.chatWindowDrawHandlers);
            chatWindowOverlayHandlers.AddRange(other.chatWindowOverlayHandlers);
            mainTabStatusHandlers.AddRange(other.mainTabStatusHandlers);
        }

        private void Add<T>(List<OrcaExtensionHandler<T>> list, string capability, T handler) where T : class
        {
            if (list == null || handler == null)
            {
                return;
            }

            list.Add(new OrcaExtensionHandler<T>(owner, capability, handler));
        }
    }
}
