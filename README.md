# RimAgent

RimAgent is a RimWorld 1.6 agent and storyteller mod. Its built-in personas, Orca Deepseek and Gemini The Cat (「Google」哈Gemi), can observe colony state, chat with the player, and plan story incidents using any configured model provider. The historical `DeepseekTheOrca` namespace and package ID remain implementation identifiers.

The mod still includes XML storyteller comps for offline play. Without a configured LLM connection, the AI decision layer stays silent and RimWorld falls back to the XML-defined storyteller behavior.

If you like my mod, please make sure to give me a Star!

## Features

- Adds the `Orca Deepseek` storyteller for RimWorld 1.6.
- Supports default connections for DeepSeek, OpenAI and Google Gemini, plus custom OpenAI-compatible `/chat/completions` APIs (including OpenRouter).
- Gemini is normally a lazy, gentle storyteller. His exclusive `gemini_hiss` tool replaces the current plan with a short punitive sequence when the model judges the player's chat offensive. Defaults: 15 budget, 5 events, an immediate first event, then 1–2 game hours between events. After the sequence he sulks for 48 game hours; the model still replies, but in character refuses meaningful conversation. Parameters are available only through Gemini's entry in the persona manager. Switching persona resets his mood but leaves an already scheduled plan in place. Hiss requires AI planning, a decision model and the RimAgent storyteller; all personas share the same offline fallback.
- Lets different model roles use different configured models: fallback, controller, decision, dialogue, tool, vision, and web search.
- Provides an in-game Orca chat window with optional tool calls.
- Can expose colony summary, recent letters, pawns, pawn details, available incidents, and incident execution tools to Orca.
- Optional Tavily web search tool for current external information.
- Optional HTTP MCP tool discovery for player-configured external tool servers.
- Optional RimTalk integration for recent chat history and proactive dialogue hooks when RimTalk is active.
- Includes English and Simplified Chinese localization.
- Optional built-in Colony novel plugin: the current narrator writes a preface and continuing chapters from locally collected game records. Defaults to one approximately 2,000-character/word chapter per three game days, using the dialogue model in two steps. Includes save-bound progress, a reader, TXT/Markdown export and extensible material sources without Harmony. See [the novel guide](Docs/Novel.md).

## Requirements

- RimWorld 1.6.
- An API key for at least one supported LLM provider if you want AI planning or Orca chat.
- Optional: Tavily API key for web search.
- Optional: RimTalk for RimTalk-aware chat history and proactive dialogue.

## Installation

1. Place this repository folder in your RimWorld `Mods` directory.
2. Enable `RimAgent`.
3. Start RimWorld and select `Orca Deepseek` as the storyteller.

## Configuration

Open RimWorld mod settings for `RimAgent`.

1. Add an LLM connection.
2. Select a provider: DeepSeek, OpenAI, Google Gemini, or Custom OpenAI-compatible.
3. Enter the provider API key.
4. Refresh available models.
5. Select models for the roles you want to use.
6. Enable AI planning if you want Orca to choose incidents automatically.

Web search, HTTP MCP, proactive dialogue, persona, skill, plugin, and debug settings are also configured from the mod settings window. Optional sub-mods can add more plugins to the same plugin settings page.

## Mod Integration

External mods can ship Orca knowledge base entries with XML Defs. See `Docs/ExternalKnowledgeSupport.md` for the supported fields, matching behavior, and a copyable template.

## Privacy And Network Use

This mod can send data outside the game when you enable external providers or tools.

- LLM requests are sent to the configured provider.
- Tavily search queries are sent to Tavily when web search is enabled.
- HTTP MCP requests are sent to the MCP endpoint URLs you configure.
- API keys and tokens are stored in RimWorld mod settings.
- RimWorld, Unity, Ludeon, DeepSeek, OpenAI, OpenRouter, Tavily, and RimTalk are separate third-party projects or services.

Only configure providers, proxies, and MCP servers you trust.

## Development

The C# project targets .NET Framework 4.8 and outputs the compiled assembly to `1.6/Assemblies`.

```powershell
dotnet build .\Source\DeepseekTheOrca\DeepseekTheOrca.sln -c Release
```

The project file currently references RimWorld assemblies under the default Steam install path:

```text
C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed
```

If your RimWorld installation is elsewhere, update the reference hint paths in `Source/DeepseekTheOrca/DeepseekTheOrca/DeepseekTheOrca.csproj`.

### Behavior Changes In The Structure Refactor

- Scheduled storyteller incidents now notify the narrative system through `OrcaProactiveConversationManager.NotifyStorytellerIncidentScheduled`, so narrative history and proactive dialogue share one path.
- Switching the active persona and toggling external skills now persist immediately via `WriteSettings`.
- The storyteller planning tool whitelist is now driven solely by the `exposeToStorytellerPlanning` flag in `OrcaToolDef` XML; tools marked in XML are actually exposed to the planning stage.

## Repository Layout

- `About/` - RimWorld mod metadata and preview image.
- `1.6/` - RimWorld 1.6 defs and compiled assemblies.
- `Languages/` - English and Simplified Chinese localization.
- `Textures/` - Orca storyteller portraits.
- `Source/` - C# solution and mod source code.

## License

This project is licensed under the MIT License. See `LICENSE` for details.

The MIT License applies to this repository's own code, XML, textures, and documentation to the extent they are owned by the project author. It does not grant rights to RimWorld, Unity, or other third-party software and services.
