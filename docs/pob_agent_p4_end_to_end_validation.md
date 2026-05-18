# Phase 4 - Prove End-to-End Communication

Location: upstream PoB fork plus existing MCPoe bridge.

Purpose: prove `exec` works through the real MCPoe-to-PoB path against live build state.

## Guardrails

- Do not change bridge protocol/framing unless explicitly approved.
- Do not start curated map work until a useful runtime readout works.
- If runtime assumptions fail, fix those before moving forward.

## Work

- Call upstream `exec` through the existing bridge path.
- Confirm C# can send Lua code and receive the structured envelope.
- Load a real build XML.
- Confirm Lua can read live build state after import.
- Confirm Lua can run at least one useful calculation readout.
- Confirm errors do not crash PoB or MCPoe.

Validation snippets to adapt as needed:

```lua
return { ok = true, lua = _VERSION }
```

```lua
return {
  name = build and build.buildName,
  level = build and build.characterLevel
}
```

```lua
local env = calcs.initEnv(build)
calcs.perform(env)
return {
  Life = env.player.output.Life,
  EnergyShield = env.player.output.EnergyShield,
  Armour = env.player.output.Armour,
  Evasion = env.player.output.Evasion
}
```

Adjust to the real call signatures discovered in the current PoB fork.

## Exit Criteria

- MCPoe can round-trip `exec` to PoB.
- A loaded build is visible to Lua.
- A useful calculation readout works.
- Failures return structured errors.

Stop and report the confirmed runtime pattern before Phase 5.
