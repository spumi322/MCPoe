# Phase 6 - Add MCPoe Tool and Resource Surface

Location: `G:/Code/MCPoe/`

Purpose: expose the working PoB substrate to MCP clients.

## Infrastructure Proposal Required

Before editing, propose the shape for:

- `Program.cs` registration
- `appsettings.json` map path key
- MCP resource registration

Recommended config key:

```json
{
  "Database": {
    "PobApiMapPath": "Resources/data/pob_api_map.json"
  }
}
```

Confirm before editing infrastructure files.

## Work

- Add `IPoBService.ExecLuaAsync(string code, CancellationToken)`.
- Implement `PoBService.ExecLuaAsync` as a thin `bridge.SendAsync("exec", new { code })` wrapper.
- Preserve the upstream `exec` envelope where practical.
- Follow existing try/catch and structured error patterns.
- Add `pob_exec_lua` to `PoBTool`.
- Add `pob_get_api_map` to `PoBTool`.
- Add `PobApiMapResource.cs`.
- Mirror the wiki DB map pattern where useful, without modifying wiki tools/services.
- Do not reintroduce removed preview reads such as `pob_get_build_info` or `pob_get_stats`.

`pob_exec_lua` description must tell agents:

- this runs arbitrary Lua against the live PoB engine,
- call `pob_get_api_map` first,
- load a build first,
- state persists across calls,
- mutations are real and should be deliberate.

## Exit Criteria

- Claude Desktop can call `pob_get_api_map`.
- Claude Desktop can call `pob_exec_lua`.
- Claude Code can read `pob-api-map://schema` if resource support is available.
- `pob_exec_lua` works against a loaded build.
- Removed preview read tools remain absent from the MCP surface.

Stop and verify both Claude Desktop and Claude Code behavior before Phase 7.
