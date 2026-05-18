# PoB Headless API Inventory

> Legacy action inventory. Useful as historical context for the old upstream headless API, but not the source of truth for the new agent-facing map. Current map work starts at `docs/pob_agent_overview.md` and `docs/pob_agent_p2_static_inventory.md`.

This inventory describes the callable surface currently exposed by the local Path of Building fork at:

- `G:/Code/utils/PathOfBuilding/src/HeadlessWrapper.lua`
- `G:/Code/utils/PathOfBuilding/src/API/Server.lua`
- `G:/Code/utils/PathOfBuilding/src/API/Handlers.lua`
- `G:/Code/utils/PathOfBuilding/src/API/BuildOps.lua`

## Runtime Contract Reference

Observed startup requirements:

- executable: `C:/Users/spumi/scoop/shims/luajit.exe`
- working directory: `G:/Code/utils/PathOfBuilding/src`
- argument: `HeadlessWrapper.lua`
- environment:
  - `POB_API_STDIO=1`
  - `LUA_PATH=<PathOfBuilding>/runtime/lua/?.lua;<PathOfBuilding>/runtime/lua/?/init.lua;;`
  - `LUA_CPATH=<PathOfBuilding>/runtime/?.dll;;`

Observed protocol:

- line-delimited JSON over stdin/stdout
- each request is `{ "action": "...", "params": { ... } }`
- successful responses contain `ok: true`
- failed responses contain `ok: false` and `error`
- startup emits non-JSON log lines before the ready JSON, so MCPoe must skip non-JSON stdout lines
- API is sequential; do not issue concurrent requests over the same process
- PoB has one mutable build context per LuaJIT process

## Live Probe Summary

Read/lifecycle probe passed:

- `ping`
- `version`
- `new_build`
- `get_build_info`
- `get_stats`
- `get_config`
- `get_tree`
- `search_nodes`
- `get_items`
- `get_skills`
- `quit`

Safe mutation probe passed:

- `set_level`
- `set_config`
- `set_tree` with empty node list
- `update_tree_delta` with empty add/remove lists
- `create_socket_group`
- `add_gem`
- `set_gem_level`
- `set_gem_quality`
- `set_main_selection`
- `remove_gem`
- `remove_skill`
- `add_item_text`

Known failed probe:

- `calc_with` currently crashes the Lua process during JSON encoding with `reference cycle`. Do not expose directly until the Lua API returns a JSON-safe projection.

## Action Inventory

| Action | Category | Params | Requires build | Changes build state | Returns | Notes |
|---|---|---|---:|---:|---|---|---|
| `ping` | lifecycle | none | no | no | `{ pong: true }` | Health check. |
| `version` | lifecycle | none | no | no | PoB/API version metadata | Version fields may contain `?` depending on launch context. |
| `quit` | lifecycle | none | no | yes, process exits | `{ quit: true }` | Gracefully stops the Lua API loop. |
| `new_build` | build lifecycle | none | no | yes | `{ ok: true }` | Creates/resets current PoB build. |
| `load_build_xml` | build lifecycle | `xml` required, `name` optional | no | yes | `{ build_id: 1 }` | Loads XML into current build context. |
| `export_build_xml` | build lifecycle | none | yes | no | `xml` string | Exports current build XML. |
| `get_build_info` | read | none | yes | no | `name`, `level`, `className`, `ascendClassName`, `treeVersion` | Basic current-build metadata. |
| `get_stats` | read/calculation | `fields` optional string array | yes | no | selected stats plus `_meta` | Missing field names are silently omitted. |
| `get_config` | read | none | yes | no | `bandit`, pantheon values, `enemyLevel` | Reads current build config. |
| `set_config` | mutation | optional `bandit`, `pantheonMajorGod`, `pantheonMinorGod`, `enemyLevel` | yes | yes | updated config | Rebuilds output after change. |
| `set_level` | mutation | `level` required, 1-100 | yes | yes | `{ ok: true }` | Rebuilds output after change. |
| `get_tree` | read | none | yes | no | tree version, class IDs, allocated node IDs, mastery effects | Reads current passive tree allocation. |
| `set_tree` | mutation | `classId`, `ascendClassId`, optional `secondaryAscendClassId`, `nodes`, `masteryEffects`, `treeVersion` | yes | yes | updated tree | Replaces full tree allocation. |
| `update_tree_delta` | mutation | optional `addNodes`, `removeNodes`, class IDs, `treeVersion` | yes | yes | updated tree | Mutates current tree from existing allocation. |
| `search_nodes` | read/search | `keyword` required, optional `nodeType`, `maxResults`, `includeAllocated` | yes | no | `{ nodes, count }` | Searches passive nodes by name/stat text. |
| `calc_with` | what-if | optional `addNodes`, `removeNodes`, `useFullDPS` | yes | no intended mutation | raw calc output | Live probe crashed with JSON reference cycle. |
| `get_items` | read | none | yes | no | equipped slots/items, raw item text, flask active flags | Returns empty/default slots on blank builds. |
| `add_item_text` | mutation | `text` required, optional `slotName`, `noAutoEquip` | yes | yes | item ID/name/slot | Text max is 10KB. |
| `set_flask_active` | mutation | `index` 1-5, `active` boolean | yes | yes | `{ ok: true }` | Slot must exist in active item set. |
| `get_skills` | read | none | yes | no | main socket group, calc skill number, socket groups, gem names | Reads skill socket groups. |
| `set_main_selection` | mutation | optional `mainSocketGroup`, `mainActiveSkill`, `skillPart` | yes | yes | updated skills summary | Also syncs calcs skill number. |
| `create_socket_group` | mutation | optional `label`, `slot`, `enabled`, `includeInFullDPS` | yes | yes | created socket group index/label | Creates a socket group in active skill set. |
| `add_gem` | mutation | `groupIndex`, `gemName` required; optional `level`, `quality`, `qualityId`, `enabled`, `count` | yes | yes | gem index/name | Gem lookup is permissive. |
| `set_gem_level` | mutation | `groupIndex`, `gemIndex`, `level` required; level 1-40 | yes | yes | `{ ok: true }` | Requires existing socket group/gem. |
| `set_gem_quality` | mutation | `groupIndex`, `gemIndex`, `quality` required; optional `qualityId`; quality 0-23 | yes | yes | `{ ok: true }` | Requires existing socket group/gem. |
| `remove_skill` | mutation | `groupIndex` required | yes | yes | `{ ok: true }` | Refuses special source-backed groups. |
| `remove_gem` | mutation | `groupIndex`, `gemIndex` required | yes | yes | `{ ok: true }` | Requires existing socket group/gem. |

## Observed Issues / Open Questions

- `calc_with` is not JSON-safe today; fix in Lua before exposing.
- `add_gem` may accept unknown gem names without strong validation.
- Full tree mutation behavior with invalid/disconnected node lists was not deeply tested.
- Large XML behavior was not load-tested.
- Process crash loses the in-memory PoB build state.
