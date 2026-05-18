# PoB Agent Substrate Overview

This is the main guide for the PoB-as-agent-substrate work. Keep it short. Put phase-specific detail in the phase files.

## Goal

Replace the brittle PoB MCP surface of many shallow handlers with an agent-driven substrate:

- PoB gains a small stable `exec` path alongside the existing build/load/export actions.
- MCPoe exposes `pob_exec_lua` and `pob_get_api_map`.
- Agents inspect a curated PoB Lua map, write small Lua snippets, execute them against the live PoB engine, and reason from structured results.
- Snippets are logged later so recurring patterns can become recipes or orchestrators.

The generated full PoB source inventory is a starter, not truth. The curated agent map should promote entries only when they are runtime-verified or clearly labeled as static/large/dangerous.

## Mental Model

```text
agent (Claude Desktop / Claude Code)
  -> MCPoe (.NET host): tools, resources, logging
       -> PoBBridge: existing action/params JSON-over-stdio conduit
            -> HeadlessWrapper.lua: PoB bootstrap
                 -> live PoB engine/build state
```

Do not blur these layers. Most logic belongs either in agent-written Lua snippets or in small MCPoe wrappers.

## Phase Order

1. [Phase 1 - Clean Redundant Tools and Leftovers](pob_agent_p1_cleanup.md)
2. [Phase 2 - Generate Static PoB Inventory](pob_agent_p2_static_inventory.md)
3. [Phase 3 - Add Exec and Safe Serialization](pob_agent_p3_exec_safe_serialize.md)
4. [Phase 4 - Prove End-to-End Communication](pob_agent_p4_end_to_end_validation.md)
5. [Phase 5 - Curate Agent API Map](pob_agent_p5_curated_api_map.md)
6. [Phase 6 - Add MCPoe Tool and Resource Surface](pob_agent_p6_mcpoe_surface.md)
7. [Phase 7 - Add Snippet Logging](pob_agent_p7_snippet_logging.md)

Stop after each phase. Report exit criteria before moving on.

## Global Guardrails

- Scope is solo, local, non-commercial learning. Bias toward the simplest thing that works.
- PoB-only work. Do not touch wiki search or PoE wiki database tools unless the user explicitly changes scope.
- Off-limits unless scope changes: `WikiSearchTool`, `PoeWikiDatabaseTool`, `PoeWikiDbService`, `WikiSearchService`, `VoyageEmbeddingService`, `PoeWikiDbMapResource`.
- Keep Claude Desktop and Claude Code working in parallel. Flag anything that works in only one client.
- Keep upstream PoB and MCPoe changes separate.
- Upstream PoB repo: `G:/Code/utils/PathOfBuilding/`.
- MCPoe repo: `G:/Code/MCPoe/`.
- Do not change the bridge protocol unless explicitly approved.

## Infrastructure Touch Points

Discuss before editing:

- `PoBBridge.cs`
- `PoBEngineManager.cs`
- bridge protocol/framing/lifecycle
- bridge `SendAsync` contract
- `Program.cs` DI registrations
- `appsettings.json` keys
- `DatabaseInitializer.cs`

The current direction is to keep the bridge protocol unchanged: `{ action, params }` JSON over stdio.

## Target Surface

Required upstream actions for the new substrate:

- `ping`
- `version`
- `new_build`
- `load_build_xml`
- `export_build_xml`
- `quit`
- new `exec`

This plan does not require pruning the upstream PoB API surface. Existing upstream handlers can remain unless a later phase proves they are unused, harmful, and safe to remove, and the user approves that cleanup.

New MCPoe surface:

- `pob_exec_lua(code)`
- `pob_get_api_map()`
- `pob-api-map://schema` MCP resource

Removed MCP tools:

- `pob_get_build_info`
- `pob_get_stats`

These preview reads under-projected live build state. The replacement path is `pob_get_api_map` plus `pob_exec_lua`.

## Agent Rules

- Follow the phase files in order.
- Prefer small changes with direct verification.
- Generated static inventory is a candidate list, not validated truth.
- Curated map entries must be verified or clearly labeled.
- C# tools should return structured errors, not raw exceptions.
- Never write to stdout from C#; use existing logging.
- Copy existing wiki-map patterns where useful, but do not modify wiki tools/services.
- Prefer read-only Lua snippets unless the user explicitly asks for mutation.

## Deferred

Do not start these in this plan revision:

- upstream orchestrator Lua modules
- separate cookbook resource
- automatic promotion from snippets to orchestrators
- final-map generation without human review
- PoB fork/submodule pinning
- sandboxing/auth/rate limiting for `exec`
- replacing the existing JSON-over-stdio framing
- sharing MCPoe over a network
