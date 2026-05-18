# Phase 5 - Curate Agent API Map

Likely location: `G:/Code/MCPoe/Resources/data/pob_api_map.json`

Purpose: turn the static inventory plus runtime validation into a compact map agents can actually use.

## Guardrails

- The generated inventory is only a starter.
- Mark unverified entries clearly.
- Do not present static-only entries as verified.
- Warn on huge or dangerous surfaces.
- Keep v1 practical: target roughly 80-150 high-value entries.

## Entry Shape

Each entry should include:

- `path`: canonical Lua path, for example `calcs.perform`
- `kind`: `function`, `table`, `field`, `class`, `method`, or similar
- `sig`: Lua signature, or `"unknown"`
- `purpose`: one concise sentence
- `status`: `verified`, `static`, `known-large`, `dangerous`, or `deprecated`
- `mutates`: boolean when relevant
- `notes`: setup, call order, warnings, object ownership
- `source`: source file or validation snippet reference when useful

Recommended top-level shape:

```json
{
  "schema_version": 1,
  "pob_version": "unknown",
  "generated_inventory": {
    "source": "static scan",
    "path": "..."
  },
  "guidance": {
    "default_pattern": "Load build, initialize calcs, return a small table.",
    "warnings": [
      "Do not return huge data tables wholesale.",
      "State persists across exec calls.",
      "Prefer read-only snippets unless the user asks for mutation."
    ]
  },
  "entries": [],
  "recipes": []
}
```

## Coverage Targets

- Globals: `build`, `main`, `data`, `launch`, `modLib`
- Calc lifecycle: `calcs.initEnv`, `calcs.perform`, `calcs.buildOutput`, `calcs.calcFullDPS`, `calcs.getCalculator` if validated
- Build read paths: `build.buildName`, `build.characterLevel`, `build.spec.curClassName`, `build.spec.curAscendClassName`, `build.spec.nodes`, `build.itemsTab.items`, `build.itemsTab.slots`, `build.skillsTab.socketGroupList`, `build.configTab.input`, `build.modDB`
- Important methods: `ModDB:NewMod`, `ModDB:Sum`, `ModDB:Flag`, `ModDB:Override`, `ModList:*`, `ModStore:*`, `Item:new`, `Item:BuildModList`, `PassiveSpec:AllocNode`, `PassiveSpec:DeallocNode`, `PassiveSpec:BuildAllDependsAndPaths`
- Data tables: `data.skills`, `data.uniques`, `data.itemBases`, `data.minions`, `data.gems`, `data.passiveTree`
- Output fields: `Life`, `LifeUnreserved`, `EnergyShield`, `Mana`, `Armour`, `Evasion`, `PhysicalDamageReduction`, `PhysicalMaximumHitTaken`, `FireResist`, `ColdResist`, `LightningResist`, `ChaosResist`, `EnduranceCharges`, `FrenzyCharges`, `PowerCharges`, `FortifyStacks`, `TotalDPS`, `FullDPS`

## Recipes

Include 3-5 inline recipes:

- read basic defenses
- list equipped items
- list active skill groups
- inspect config inputs
- non-committing what-if pattern, only if validated

## Exit Criteria

- An agent given only the curated map can write a working defense-readout snippet on first try.
- Static-only entries are labeled.
- Huge/dangerous entries include filtering or mutation guidance.

Stop and test the map with real prompts before Phase 6.
