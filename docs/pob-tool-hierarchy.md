# PoB Tool Hierarchy

> Legacy tool hierarchy. This categorizes the old headless action surface and should not drive new tool implementation. Current PoB agent-substrate work starts with `docs/pob_agent_overview.md`.

This document categorizes the existing PoB headless API actions from `docs/pob-api-inventory.md`.

It also lists implemented MCPoe custom wrapper tools where they replace raw headless actions.

## 1. Session

Administrative actions for the PoB engine session and current build session.

```text
ping
version
quit
new_build
export_build_xml
pob_import_build
```

Notes:

```text
ping
  Health check.

version
  Runtime/API version metadata.

quit
  Gracefully stops the Lua API loop.

new_build
  Creates or resets the current build.

export_build_xml
  Exports current build XML.

pob_import_build
  Implemented MCPoe custom import tool.
  Classifies the source, accepts local .xml files in the current preview, reads XML from disk, and loads it through the internal load_build_xml action.
```

## 2. Read

Read-only build inspection actions.

```text
get_build_info
get_stats
get_config
get_tree
search_nodes
get_items
get_skills
```

Notes:

```text
get_build_info
  Basic current-build metadata.

get_stats
  Calculated stats, optionally limited by field names.

get_config
  Current config tab values.

get_tree
  Current passive tree allocation.

search_nodes
  Passive tree node search.

get_items
  Current items and equipped slots.

get_skills
  Current skill socket groups and gems.
```

## 3. Edit

Build mutation actions.

```text
set_config
set_level
set_tree
update_tree_delta
add_item_text
set_flask_active
create_socket_group
add_gem
set_gem_level
set_gem_quality
set_main_selection
remove_gem
remove_skill
```

Notes:

```text
set_config
  Mutates config tab values.

set_level
  Mutates character level.

set_tree
  Replaces full passive tree allocation.

update_tree_delta
  Adds/removes passive tree nodes from current allocation.

add_item_text
  Adds item text, optionally equipping it.

set_flask_active
  Mutates flask active state.

create_socket_group
  Creates a skill socket group.

add_gem
  Adds a gem to a skill socket group.

set_gem_level
  Mutates gem level.

set_gem_quality
  Mutates gem quality.

set_main_selection
  Mutates main skill/socket selection.

remove_gem
  Removes a gem from a socket group.

remove_skill
  Removes a skill socket group.
```

## 4. Workflows

Placeholder for future custom toolchains.

No existing actions from `docs/pob-api-inventory.md` are categorized here.

## 5. Debug / Internal

Actions that should not be exposed as normal Claude-facing tools in their raw form.

```text
calc_with
load_build_xml
```

Notes:

```text
calc_with
  Currently not JSON-safe. Live probe crashed with a reference cycle.
  Keep blocked until the Lua API returns a safe projection.

load_build_xml
  Raw headless action for loading XML text.
  Keep internal so Claude uses pob_import_build instead of sending raw XML through chat.
```
