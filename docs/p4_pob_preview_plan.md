# PoB Integration Preplan

> Historical preview plan. This foundation work has been superseded by the PoB agent-substrate plan. For current work, start with `docs/pob_agent_overview.md`.

## Goal

Build the PoB integration foundation for MCPoe:

- reliable communication with the real Path of Building headless Lua engine
- one global PoB engine session for the lifetime of the running MCPoe process
- basic build XML load, inspect, calculate, and export tools

MCPoe does not calculate Path of Building logic itself. It calls the real PoB engine and returns structured PoB-backed results for the LLM.

```text
Claude
  -> MCPoe.exe
    -> PoBEngineManager
      -> long-lived LuaJIT + HeadlessWrapper.lua
        -> real PoB calculation engine
```

Use `docs/pob-api-inventory.md` as the reference for the actual callable PoB API surface.

## Current Phase Scope

This phase is a foundation proof of capability, not full PoB tool coverage.

In scope:

- `PoBBridge`
- `PoBEngineManager`
- minimal `PoBService`
- migration decision for the existing `calculate_build` tool/test surface
- basic MCP tools:
  - `pob_status`
  - `pob_new_build`
  - `pob_load_build_xml`
  - `pob_get_build_info`
  - `pob_get_stats`
  - `pob_export_build_xml`

The phase is complete when MCPoe can:

1. start the PoB headless engine
2. keep it alive across multiple tool calls while MCPoe is running
3. load raw PoB XML or create a new build
4. return build info
5. return calculated stats
6. export the current build XML
7. survive normal tool errors with structured MCPoe responses
8. shut down LuaJIT cleanly when MCPoe stops

## Out Of Scope

For this phase:

- wrappers for all PoB API actions
- PoB share-code import, including base64/zlib-deflated codes and `pobb.in` URLs
- optimization/advice logic
- gateway/toolmap
- abbreviation tool
- cross-tool orchestration
- description fine-tuning
- export/save reminders or other QOL behavior
- `calc_with`
- rewriting PoB in C#

After this phase, adding additional PoB tools should be thin-wrapper work on top of the bridge/engine foundation.

## Session Model

There is one global PoB session per running MCPoe process.

Do not design conversation isolation. MCP transport does not provide a reliable "one conversation equals one server session" boundary.

Accurate framing:

```text
PoB state is process-scoped. It lives until MCPoe restarts, LuaJIT restarts, or the engine is explicitly reset.
```

The LuaJIT process holds the real current build state in memory. MCPoe tracks only minimal metadata for control and response context.

Example supported flow for this phase:

```text
create or load build XML
inspect build info
get calculated stats
export build XML
```

## Architecture

```text
PoBBridge
  how to talk to LuaJIT

PoBEngineManager
  what is going on with the current process-scoped PoB session

PoBService
  tool-facing operations

MCP Tools
  Claude-facing entrypoints
```

## PoBBridge

Lowest layer. Dumb transport.

Responsibilities:

- start `luajit.exe`
- working directory: `PathOfBuilding/src`
- argument: `HeadlessWrapper.lua`
- set required environment:
  - `POB_API_STDIO=1`
  - `LUA_PATH`
  - `LUA_CPATH`
- wait for ready JSON
- send one JSON action per line
- read one JSON response
- skip non-JSON stdout
- add startup and per-request timeouts
- detect process exit during a request
- quit or kill the process

No knowledge of build state or tool meaning.

Note: per-request timeout is new work. The current `SendAsync` path does not enforce its own request timeout. This must not be missed.

## PoBEngineManager

Process-scoped state/session layer.

Responsibilities:

- lazy-start LuaJIT on first PoB tool call
- keep one long-lived PoB process while MCPoe is running
- serialize all calls with a lock/queue
- track minimal session state:
  - `EngineAlive`
  - `HasLoadedBuild`
  - `BuildName`
  - `LastAction`
  - `EngineStartedUtc`
- classify only the actions needed for this phase:
  - lifecycle: `ping`, `version`, `quit`
  - creates/loads build: `new_build`, `load_build_xml`
  - requires loaded build: `get_build_info`, `get_stats`, `export_build_xml`
  - blocked: `calc_with`
- reject build-dependent actions when no build is loaded
- include session metadata in PoB tool responses
- implement `IAsyncDisposable`
- quit/kill LuaJIT during host shutdown

Crash behavior decision:

- If the engine is dead on the next PoB call, lazily restart it.
- Return a structured message that the previous in-memory PoB build state was lost.
- After restart, `HasLoadedBuild=false`; the user/LLM must create or load a build again.

No dirty/export reminder logic in this phase.

## PoBService

Application layer.

Responsibilities:

- validate tool inputs
- call `PoBEngineManager`
- combine small multi-action operations where useful
- shape MCPoe JSON responses
- return raw PoB-backed facts, not advice

Initial methods:

- `GetStatusAsync`
- `NewBuildAsync`
- `LoadBuildXmlAsync`
- `GetBuildInfoAsync`
- `GetStatsAsync`
- `ExportBuildXmlAsync`

Existing surface migration:

- Current `IPoBService.CalculateNewBuildAsync` and `calculate_build` must not be silently broken.
- For this phase, keep `calculate_build` as a thin compatibility alias over the new `NewBuildAsync` + `GetBuildInfoAsync` + `GetStatsAsync` flow, or explicitly remove it with test updates.
- Decide this before implementation. Default recommendation: keep it as an alias until the new tools are proven.

## MCP Tools

Thin wrappers only.

Initial tools:

- `pob_status`
- `pob_new_build`
- `pob_load_build_xml`
- `pob_get_build_info`
- `pob_get_stats`
- `pob_export_build_xml`

No other PoB API actions are added in this phase.

Important scope wording:

- `pob_load_build_xml` loads raw PoB XML.
- It is not PoB share-code import.
- It does not cover `pobb.in` URLs or compressed/base64 build codes.

## Implementation Order

1. Harden `PoBBridge`
   - process start
   - ready wait
   - JSON line protocol
   - non-JSON stdout skipping
   - startup timeout
   - per-request timeout
   - process exit detection
   - quit/kill

2. Add `PoBEngineManager`
   - lazy singleton process
   - lock/queue
   - session metadata
   - action classification for the initial tool set
   - blocked action handling
   - lazy restart after crash with state-loss reporting
   - `IAsyncDisposable`
   - host shutdown cleanup

3. Add minimal `PoBService`
   - status/new/load XML/get info/get stats/export XML
   - decide and handle `calculate_build` migration

4. Add first MCP tools
   - direct, thin wrappers

5. Verify the end-to-end flow
   - MCP tools list correctly
   - `pob_status` starts/reports engine health
   - `pob_new_build` creates a build
   - `pob_get_build_info` works after build creation
   - `pob_get_stats` returns calculated stats
   - `pob_export_build_xml` returns XML
   - old `calculate_build` behavior is intentionally preserved or intentionally removed
   - LuaJIT process is cleaned up when MCPoe stops
