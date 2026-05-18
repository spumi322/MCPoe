# Phase 2 - Generate Static PoB Inventory

Location: primarily `G:/Code/utils/PathOfBuilding/src/`

Purpose: use the full PoB repo as a broad source-derived starter map.

This inventory is not the final agent-facing API map. It is a candidate list for validation and curation.

## Guardrails

- Upstream/source inspection work only.
- Prefer a repeatable script or generator over hand collection.
- Preserve uncertainty. Do not invent signatures static analysis cannot know.
- Mark generated entries as static/candidate.

## Work

- Scan Lua source for:
  - globals
  - module returns
  - class-like tables
  - methods
  - data tables
  - calculation entry points
- Include source file and approximate line/reference when possible.
- Identify high-value areas first:
  - `build`
  - `calcs`
  - `ModDB`, `ModList`, `ModStore`
  - `Item`
  - passive spec/tree APIs
  - skills/gems
  - `data.*`
  - calculation output fields

Recommended raw shape:

```json
{
  "generated_at": "ISO-8601",
  "source": {
    "repo": "G:/Code/utils/PathOfBuilding",
    "method": "static-scan"
  },
  "entries": [
    {
      "path": "calcs.perform",
      "kind": "function",
      "sig": "unknown",
      "source_file": "src/Modules/CalcPerform.lua",
      "status": "static",
      "notes": "Candidate only; validate at runtime."
    }
  ]
}
```

## Exit Criteria

- A broad static inventory exists.
- It distinguishes known source facts from guesses.
- It is useful enough to guide runtime validation and curated map writing.

Stop and review inventory quality before Phase 3.
