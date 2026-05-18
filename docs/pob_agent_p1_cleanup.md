# Phase 1 - Clean Redundant Tools and Leftovers

Location: primarily `G:/Code/MCPoe/`, with read-only inspection of `G:/Code/utils/PathOfBuilding/` if needed.

Purpose: remove or clearly mark redundant MCPoe-facing PoB tools, stale leftovers, and misleading docs before building the new substrate.

This is not an upstream pruning phase. Do not reduce the PoB fork's handler set just because the new design will prefer `exec`.

## Guardrails

- Do not change bridge protocol/framing/lifecycle.
- Do not edit `PoBBridge.cs`, `PoBEngineManager.cs`, `Program.cs`, `appsettings.json`, or `DatabaseInitializer.cs` without a short proposal first.
- Do not touch wiki search or PoE wiki database tools/services.
- Do not delete upstream PoB handlers in this phase.
- Public MCP tools should not disappear before their replacement path exists unless the user explicitly accepts that break.

## Work

- Audit the current PoB-facing MCPoe surface:
  - `pob_status`
  - `pob_new_build`
  - `pob_import_build`
  - `pob_load_build_xml`
  - `pob_get_build_info`
  - `pob_get_stats`
  - `pob_export_build_xml`
- Identify tools that are misleading, under-projecting, redundant, or deprecated.
- Identify stale PoB planning docs, preview artifacts, unused helper code, or dead tests that would confuse future agents.
- Remove only leftovers that are demonstrably unused and have no runtime or client-facing contract.
- Remove deprecated MCP tools once the user explicitly accepts the breaking cleanup.
- Record any upstream PoB handler cleanup candidates separately instead of deleting them.

## Recommendations

- Remove `pob_get_build_info` and `pob_get_stats`; they under-project build state and are deprecated.
- Keep `pob_status`, `pob_new_build`, `pob_import_build`, `pob_load_build_xml`, and `pob_export_build_xml` unless there is concrete evidence they are broken or unused.
- If cleanup touches code, run the narrowest useful build/test command available.
- If cleanup is docs-only, verify links and file names.

## Exit Criteria

- There is no misleading Phase 1 instruction to prune upstream PoB handlers.
- Redundant/deprecated MCPoe-facing PoB tools are identified and removed when approved.
- Safe leftovers are removed or renamed.
- Anything not safe to remove yet is explicitly deferred with a reason.
- MCPoe still builds or the phase is clearly docs-only.

Stop and report what was removed, what was deferred, and why before Phase 2.
