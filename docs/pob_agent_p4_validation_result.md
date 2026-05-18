# Phase 4 Validation Result

Date: 2026-05-18

Status: passed.

## Confirmed

- MCPoe can send upstream `exec` through the existing `PoBEngineManager.ExecuteAsync` bridge path.
- No bridge protocol or framing changes were needed.
- A build imported through `pob_import_build` is visible to Lua snippets through global `build`.
- Runtime build state includes at least:
  - `build.buildName`
  - `build.characterLevel`
  - `build.itemsTab`
  - `build.skillsTab`
  - `build.calcsTab`
- A useful calculation readout works through `build.calcsTab:BuildOutput()` and `build.calcsTab.mainOutput`.
- Runtime errors from snippets return structured `{ ok = false, error, traceback }` envelopes and do not kill the bridge.

## Confirmed Snippet Pattern

Direct global `calcs` was not required for the first readout. The validated path is:

```lua
if build.calcsTab.BuildOutput then
  build.calcsTab:BuildOutput()
end
local out = build.calcsTab.mainOutput or {}
return {
  Life = out.Life,
  EnergyShield = out.EnergyShield,
  Armour = out.Armour,
  Evasion = out.Evasion
}
```

This should be treated as the first practical runtime pattern for Phase 5 map curation.

## Verification

Added integration coverage in `MCPoe.Tests/PoBBridgeIntegrationTests.cs`:

- `Pob_exec_round_trips_through_bridge_and_reads_loaded_build`

Commands run:

```powershell
dotnet test .\MCPoe.Tests\MCPoe.Tests.csproj --filter FullyQualifiedName~Pob_exec_round_trips_through_bridge_and_reads_loaded_build
dotnet test .\MCPoe.Tests\MCPoe.Tests.csproj
```

Result:

- targeted test: passed
- full test project: 29 passed, 0 failed
