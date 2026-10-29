# Connect an external agent to RimAgent

This guide is for an agent asked by the player to connect to their local RimWorld game. RimAgent supplies a stdio MCP server and an in-game bridge. Enabling the bridge does not register the server in your client. You need local file access and permission to configure the client; a text-only model cannot perform these steps itself.

## 1. Find the installed mod

If you are reading the installed copy of this file, the MCP host should be in the same directory. Otherwise, locate the player's RimWorld installation and Steam library folders. Check the game's `Mods` directory and its Steam Workshop content directory. Use configured Steam library locations rather than assuming the game is on drive C. Avoid a full-disk search when these locations are available.

Identify the mod by `About/About.xml`:

- Name: `RimAgent`
- Package ID: `RedstonePanda.Orca` (retained for compatibility)
- Supported game version: `1.7`

Do not rely on the folder name: local installations may still be named `DeepseekTheOrca`, and Workshop folders may have numeric names. Prefer the installation actually enabled in the player's game, not an old development or backup copy. If multiple installations remain ambiguous, ask the player which game instance to use.

Under the mod root, locate:

```text
MCP/RimAgent.McpHost.exe
MCP/RimAgent.McpHost.dll
MCP/RimAgent.Core.dll
MCP/RimAgent.McpHost.deps.json
MCP/RimAgent.McpHost.runtimeconfig.json
```

Keep these files together. The separate `1.7/Assemblies/RimAgent.Core.dll` belongs to the game and must also remain in place. A source checkout needs to be built before its MCP host is available.

## 2. Check the runtime and game bridge

The standalone host requires the .NET 10 runtime. Check `dotnet --list-runtimes` if `dotnet` is available. A missing command does not alone prove that no runtime is installed; check the installed runtime location or the host's startup diagnostic. The game's bundled runtime does not by itself satisfy the standalone host's requirement. Report a missing runtime and follow the player's software-installation permissions rather than silently installing it.

In the game, enable **External agent bridge** in RimAgent settings. This takes effect without restarting the game. `running: RimAgent-...` means the in-game pipe is listening; it does not mean that a client has connected. **External HTTP MCP** is a different setting for tools the mod calls and is not needed for this connection.

The game and MCP host must run as the same OS user. The default pipe is `RimAgent-<OS user name>`. For a custom instance, pass `--pipe` followed by the exact pipe name shown by the game. Do not guess a pipe when several instances are running.

## 3. Register the server in your client

Inspect your client's installed MCP management tools, CLI help, or documented configuration format. Do not assume that another client's JSON or TOML format applies. Use its supported add/update operation when available; otherwise edit only the relevant server entry, preserving other servers, credentials, and settings. Reuse an existing RimAgent entry instead of creating duplicates.

Use these transport and launch values, substituting a verified absolute path:

```text
Transport: stdio
Command: <absolute mod path>/MCP/RimAgent.McpHost.exe
Arguments: none by default
```

An equivalent launch is:

```text
Command: <resolved dotnet executable>
Arguments: ["<absolute mod path>/MCP/RimAgent.McpHost.dll"]
```

Supply arguments as separate values in the client's format so paths containing spaces remain intact. This is not an HTTP endpoint and requires no port or bearer token. Do not launch an unrelated detached host: the MCP client must own the host's standard input and output. A manually launched host waiting silently for input is not proof of failure.

Refresh the client's MCP connection using its supported mechanism. Some clients require a new conversation or a client restart before newly registered tools appear. If you cannot refresh the current conversation, report the exact remaining step; do not claim the connection is ready just because a configuration file was saved.

## 4. Verify the connection without taking control

1. Complete MCP initialization and tool discovery. Confirm `rimagent_session_get` is present. Discovery alone checks the host, not the game connection.
2. Call `rimagent_session_get` with empty arguments. A successful result verifies a round trip to the running game. Record the returned `sessionId` and readiness information.
3. `ready: false` at the main menu is expected. The bridge is connected, but colony operations require a loaded game. Do not create or replace a save just to test connectivity.
4. If the call reports `bridge_unavailable`, check that the game is running, the bridge is enabled, both processes use the same OS user, and the pipe name matches. During game loading, wait and retry the read-only query.

Report these stages separately: server registered, host initialized, game bridge reachable, and colony ready. Never report a successful connection solely from the process existing or the tool list appearing.

## 5. Take control only for the player's requested role

A connection test does not require a controller lease. If the player has also requested control, select exactly one mode:

- `operation`: act as the player and manage the colony.
- `narrative`: replace the narrative controller and serve game chat/story requests. Acquiring this mode interrupts active internal chat/planning work.

If the intended role is unclear, ask before acquiring control. Use separate conversations and separate MCP host connections when both roles are wanted. The mod permits one active controller per mode; do not displace another controller to complete a connection test.

Read the live tool schemas, then call `rimagent_controller_acquire` with the current session, mode, owner, and a new request ID. Retain the lease token privately, track the returned history epoch, renew the lease while working, and release it when finished. After loading another save, query the session again. For ambiguous mutation results, retry with the same request ID, arguments, and epoch.

Narrative controllers must keep handling `rimagent_requests_wait` / `rimagent_request_complete`; the mod cannot wake a stopped agent conversation. External control does not require an in-mod LLM provider. Optional in-mod model workflows still need their own configuration.

## Suggested player prompt

> Connect to my running RimWorld game through RimAgent. Find the enabled RimAgent installation and read its MCP/CONNECT.md. Configure this client's stdio MCP entry using the verified host path, preserve existing client settings, and verify the connection with rimagent_session_get. Report whether the colony is ready. Do not take control or change the save yet. If this conversation cannot load newly registered MCP tools, tell me the exact refresh or restart step required.
