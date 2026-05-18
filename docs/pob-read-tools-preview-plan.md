# PoB Read Tools Preview Plan

> Superseded plan. Do not implement broad hand-written PoB read tools from this document. Current direction is `pob_get_api_map` plus `pob_exec_lua`; start with `docs/pob_agent_overview.md`.

## Goal

Prepare the PoB MCP tool surface for bulk read-tool implementation.

This plan covers three separate tasks:

```text
1. Introduce the updated global MCPoe response envelope.
2. Hide raw load_build_xml from Claude.
3. Implement the PoB read tools from docs/pob-api-inventory.md.
```

Wiki search and database tools are out of scope for this plan except where the global envelope model affects shared response code.

## Response Envelope Refactor

### Decision

Use one global outer envelope for all MCPoe tools.

Drop tool-returned natural-language instructions for now.

Reason:

```text
Tool-returned instructions may be treated by the LLM as untrusted/injection-like text.
Structured metadata is enough for routing and interpretation.
```

### New Outer Envelope

Target shape:

```json
{
  "status": "ok",
  "tool": "pob_get_stats",
  "metadata": {
    "domain": "pob",
    "category": "read",
    "pobState": {}
  },
  "results": [],
  "error": null
}
```

For errors:

```json
{
  "status": "error",
  "tool": "pob_get_stats",
  "metadata": {
    "domain": "pob",
    "category": "read",
    "pobState": {}
  },
  "results": [],
  "error": {
    "reason": "No PoB build is loaded."
  }
}
```

### Remove From Envelope

Remove these fields from the global response model:

```text
grounded
mustAnswerFromResults
instruction
query
```

If a tool needs query/input echo later, put it in metadata explicitly.

### Metadata Convention

Required metadata:

```text
domain
  pob, wiki, db, etc.

category
  session, read, edit, workflow, debug_internal, etc.
```

PoB metadata should include:

```text
pobState
  Current PoB engine/build state snapshot.
```

Import metadata should include:

```text
import
  Source classification and resolved path metadata.
```

### Result Convention

Use category-specific result payloads inside the shared envelope.

PoB read results should keep the underlying PoB response visible, but add a stable `kind` marker:

```json
{
  "kind": "pob.stats",
  "ok": true,
  "stats": {},
  "_meta": {}
}
```

Keep result payloads close to the existing Lua API response unless the raw shape is too noisy.

### Implementation Steps

1. Update `McpToolResponse` model.
2. Update existing response serialization call sites.
3. Add helper methods for PoB response metadata:

```text
domain = "pob"
category = ...
pobState = ...
```

4. Update tests that assert old envelope fields.
5. Verify existing tools still return JSON accepted by Claude Desktop and MCP Inspector.

## Hide Raw XML Load From Claude

### Decision

`load_build_xml` remains an internal Lua/headless action.

`IPoBService.LoadBuildXmlAsync(...)` may remain as internal application service code.

Do not expose `pob_load_build_xml` as an MCP tool.

### Reason

Claude bypassed `pob_import_build` and called `pob_load_build_xml` directly.

Descriptions are not strong enough routing controls.

The intended user-facing load path is:

```text
pob_import_build
```

### Implementation Steps

1. Remove `[McpServerTool]` exposure for `pob_load_build_xml`.
2. Keep the method available internally if useful for service reuse/tests.
3. Keep XML log redaction in `PoBBridge` for `load_build_xml`.
4. Update docs/tool hierarchy to mark `load_build_xml` internal.
5. Verify MCP Inspector no longer lists `pob_load_build_xml`.

### Exposed Session Tools After This Step

Keep exposed:

```text
pob_status
pob_import_build
pob_new_build
pob_export_build_xml
```

Open question for later:

```text
Whether pob_new_build and pob_export_build_xml should stay exposed in normal Claude Desktop use.
```

Do not change them in this task unless testing shows they cause routing problems.

## Implement PoB Read Tools

### Existing Read Actions

From `docs/pob-api-inventory.md`:

```text
get_build_info
get_stats
get_config
get_tree
search_nodes
get_items
get_skills
```

Already exposed:

```text
pob_get_build_info
pob_get_stats
```

Add:

```text
pob_get_config
pob_get_tree
pob_search_nodes
pob_get_items
pob_get_skills
```

### Tool Contracts

#### pob_get_build_info

Category:

```text
read
```

Lua action:

```text
get_build_info
```

Params:

```text
none
```

Requires loaded build:

```text
yes
```

Result kind:

```text
pob.build_info
```

#### pob_get_stats

Category:

```text
read
```

Lua action:

```text
get_stats
```

Params:

```text
fields?: string[]
```

Requires loaded build:

```text
yes
```

Result kind:

```text
pob.stats
```

#### pob_get_config

Category:

```text
read
```

Lua action:

```text
get_config
```

Params:

```text
none
```

Requires loaded build:

```text
yes
```

Result kind:

```text
pob.config
```

#### pob_get_tree

Category:

```text
read
```

Lua action:

```text
get_tree
```

Params:

```text
none
```

Requires loaded build:

```text
yes
```

Result kind:

```text
pob.tree
```

#### pob_search_nodes

Category:

```text
read
```

Lua action:

```text
search_nodes
```

Params:

```text
keyword: string
nodeType?: string
maxResults?: int
includeAllocated?: bool
```

Requires loaded build:

```text
yes
```

Result kind:

```text
pob.tree_node_search
```

#### pob_get_items

Category:

```text
read
```

Lua action:

```text
get_items
```

Params:

```text
none
```

Requires loaded build:

```text
yes
```

Result kind:

```text
pob.items
```

#### pob_get_skills

Category:

```text
read
```

Lua action:

```text
get_skills
```

Params:

```text
none
```

Requires loaded build:

```text
yes
```

Result kind:

```text
pob.skills
```

### Implementation Steps

1. Add `IPoBService` methods for missing read tools.
2. Add `PoBService` wrappers that call `ExecuteToolAsync(...)`.
3. Add `PoBTool` MCP methods.
4. Ensure each response metadata uses:

```text
domain = "pob"
category = "read"
pobState = current PoB engine/build state
```

5. Add result `kind` markers for read tool responses.
6. Extend integration test:

```text
import or create build
call every read tool
assert status ok
assert result kind
assert expected top-level fields exist
```

7. Verify in MCP Inspector that the exposed PoB read tool list is coherent.

## Suggested Execution Order

Implement in this order:

```text
1. Envelope refactor.
2. Hide pob_load_build_xml.
3. Add missing read tools.
4. Run tests.
5. Publish dist.
6. Verify exposed tool list in MCP Inspector.
7. Test read tools from Claude Desktop.
```

Reason:

```text
The read tools should be added after the envelope is settled so we do not duplicate response-shape churn.
```
