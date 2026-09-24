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
- Optional built-in Colony novel plugin: the current narrator writes a preface and continuing chapters from locally collected game records. Defaults to one approximately 2,000-character/word chapter per three game days, using the dialogue model in two steps. Includes save-bound progress, a reader, TXT/Markdown export and extensible material sources without Harmony.

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

With AI planning enabled and a decision model configured, planning starts automatically. A successful connection test is not required. Connection status reports do not replace or cancel a running planner; each workflow handles its own request failures.

Web search, HTTP MCP, proactive dialogue, persona, skill, plugin, and debug settings are also configured from the mod settings window. Optional sub-mods can add more plugins to the same plugin settings page.

## Mod Integration

External mods can ship Orca knowledge base entries with XML Defs and register behavior through `OrcaExtensionWorker.Register`.

See [the persona and plugin API guide](Docs/Extensions.md) for XML personas with optional DLL behaviors, persona-exclusive tools, per-game state, custom configuration and lifecycle hooks. Built-in Gemini uses the same public behavior and module-data interfaces.

Novel sources register with `registry.AddNovelSource(() => new YourSource())`. Implement `INovelSource.Scan`, yielding records or null checkpoints on the main thread. `NovelCaptureContext` provides source-scoped `Seen`, `State`, `Baseline`, `BaselineKeys`, `RemoveBaseline`, `HasRecord`, `Diagnostic` and `ClearDiagnostic`; it does not expose the book or chapters. Keys passed to these methods are local to the source. Optional `INovelSourceReadiness.PrepareForWriting` supplies incremental cache repair before requests; failure pauses writing until manual recovery.

Skills can declare `taskScopes` in their metadata: `chat`, `novel_organize`, `novel_write`, or extension-defined task names. Missing/empty scopes preserve legacy chat behavior. Scopes restrict which task may load the skill; `contexts` remain relevance hints within a task. The skill editor exposes this field. Novel generation captures all enabled matching skills once per chapter, separately for each stage, independent of skill name or source mod. Disabled skills contribute no instructions; missing scopes or empty enabled instructions report a configuration error. Existing chapters retain their saved instruction snapshots.

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

### Runtime Boundaries

- Ordinary chat retains controller-led routing when a controller model is configured. Persona constraints may require dialogue only (for example Gemini's cooldown). Without a controller, local routing remains available. Incident planning and novel writing are independent background workflows.
- `OrcaPersonaBehavior` supplies runtime prompts, routing constraints, execution restrictions, selection lifecycle and settings entry points. Additional personas register policies through `OrcaPersonaBehaviors.Register`. Gemini's implementation owns its special decisions; shared chat code consumes the behavior interface.
- The incident planner accepts `IncidentPlanningPolicy` rather than concrete persona settings. The schedule uses the generic `finishWhenEventsComplete` field and save key. This release intentionally does not migrate old persona/plugin state or preserve replaced extension APIs.
- `NovelGameComponent` owns game lifetime, persistence and coordination. `NovelCollectionScheduler` handles scanning and readiness; `NovelGenerationRunner` handles requests, configuration snapshots and failure recovery; `NovelWriting` handles indexes and response protocols. Sources cannot directly mutate chapter state.
- Background generation uses the shared request scheduler and yields admission to waiting foreground requests. Active requests complete normally. Game-instance checks prevent stale completions from entering another save.
- Chat workflows advance through game updates even with all chat windows closed. Remote tools and semantic query preparation yield while waiting; their results are consumed on the main thread.
- Memory compaction waits 30 seconds and then 120 seconds between failures, with at most three total attempts before manual resume. Retry progress persists per persona. Missing or rebuilding embeddings retain keyword retrieval. Changing the embedding model schedules gradual background vector rebuilding, which uses the newly configured provider and may incur API costs.
- Memory files use atomic replacement and a replayable journal for compaction. File access errors or corrupt primary records pause memory collection and requests; manual resume retries local persistence before generating again. Pending in-memory changes remain owned by their original persona. Changes that cannot be written do not survive process exit, so resolve the displayed storage error before quitting.
- Extension callbacks receive copied `OrcaChatSnapshot` values rather than mutable chat sessions. Persona policies, plugin hooks, and proactive/narrative sources isolate failing callbacks. Submods can unregister their sources when disabled.
- RimTalk proactive scans select the newest records before expanding their text, using O(N log K) selection and O(K) retained references (K = 30). Disabled ambient dialogue does not scan this history. Novel collection still retains all captured evidence.
- Novel scan budgets are shared across tick/update calls within a frame. Skill catalogs are cached for five seconds; explicit reload invalidates them immediately, and reference text is invalidated when file metadata changes.
- RimTalk text conversion, pawn checks and building counts yield incrementally. Raw game collections still require reference snapshots, and external API calls cannot be forcibly preempted: the 2 ms scan budget is a cooperative target, not a strict frame-time guarantee.

## Repository Layout

- `About/` - RimWorld mod metadata and preview image.
- `1.6/` - RimWorld 1.6 defs and compiled assemblies.
- `Languages/` - English and Simplified Chinese localization.
- `Textures/` - Orca storyteller portraits.
- `Source/` - C# solution and mod source code.

## License

This project is licensed under the MIT License. See `LICENSE` for details.

The MIT License applies to this repository's own code, XML, textures, and documentation to the extent they are owned by the project author. It does not grant rights to RimWorld, Unity, or other third-party software and services.
