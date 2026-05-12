# MCPoe -- P1b: Mods DB (Proof of Concept)

## Context

MCPoe is a C# MCP server (.NET 10, stdio transport) for Path of Exile build analysis. P0 and P1a are complete: the solution builds, has Serilog file logging, interface-driven DI, and a working headless PoB bridge.

P1b is a **proof of concept**. The only goal is to validate that a local SQLite database can store PoE item/mod data from the wiki's Cargo API, and that an MCP tool can query it and return useful results. Keep it minimal. One item, one tool, prove the pipeline.

## Architecture

```
poewiki.net Cargo API → (one-time import) → mods.db (SQLite) → lookup_item tool → Claude
```

The Cargo API is read-only, public, no auth needed. We query it once, dump the data into SQLite, then the MCP tool queries locally. No runtime dependency on the wiki.

## The Cargo API

Base URL: `https://www.poewiki.net/w/api.php?action=cargoquery`

Response format -- every query wraps rows like this:
```json
{"cargoquery": [{"title": {...fields...}}, {"title": {...fields...}}]}
```

**IMPORTANT:** The JSON keys in responses use **spaces** where the query fields use **underscores**. Example: you query `class_id` but the response key is `"class id"`. Your deserializer must handle this mapping.

**IMPORTANT:** Fields starting with `_` (like `_pageID`, `_pageName`) cause API errors unless aliased. Example: `items._pageID=pageID` in the fields parameter.

Pagination: `limit` (max 500) + `offset` parameters.

## Database Schema

Use the DDL from the `mods-db-schema` artifact. It defines 9 tables derived from confirmed live API responses. The schema has been validated against real data.

The 5 tables with real data for the POC item:
- `items` -- core item data (name, class, rarity, base item, drop level, stat text, etc.)
- `item_mods` -- which mods are on an item (mod_id, is_implicit, is_random)
- `item_stats` -- aggregated stat values per item (stat_id, min, max)
- `mods` -- modifier definitions (id, stat_text, generation_type, domain)
- `mod_stats` -- per-mod stat ranges (mod_id, stat_id, min, max)
- `item_sell_prices` -- vendor sell price (name, amount)

The remaining 3 tables (spawn_weights, legacy_variants, item_purchase_costs) returned all nulls for this item. Create the tables but don't worry about seeding them.

**NOTE on `item_stats`:** The same `stat_id` can appear multiple times for one item. Headhunter has `base_maximum_life` twice -- once from the implicit mod (25-40) and once from an explicit mod (50-60). Do NOT use `(page_id, stat_id)` as a composite primary key.

## Seed Data

The seed data is one item: **Headhunter** (page_id 8042, unique Leather Belt). All API responses have been captured and validated. The raw JSON responses are available in the chat history. Seed all 6 tables from that data.

Key data points for verification:
- 6 mods total (1 implicit: IncreasedLifeImplicitBelt1, 5 explicit)
- 6 stat entries (base_maximum_life appears twice)
- 6 mod_stat entries
- 2 sell price entries (Alchemy Shard: 10, Alteration Shard: 9)
- Iconic mod: "When you Kill a Rare monster, you gain its Modifiers for 60 seconds"

## The lookup_item Tool

Add a new MCP tool to the existing MCPoe server. It takes an item name and returns everything we know about that item from the database -- the item data, its mods with stat text, and its stat ranges.

Guidelines:
- The tool should join across the relevant tables to build a complete picture
- Return enough information that an LLM can answer natural language questions like "what mods does Headhunter have?" or "what is the life roll range on Headhunter?"
- Clean up the wiki markup in stat_text (strip `[[links]]`, `<br>` tags, HTML entities) before returning to the LLM -- raw HTML is noise
- The tool description should tell the LLM what it can query and what kind of questions it answers

## What NOT to Do

- No bulk import logic. We are seeding one item manually. The Cargo API bulk importer is a future task.
- No production error handling beyond what is needed to not crash.
- No caching layer. SQLite is fast enough for a POC.
- No schema migrations or versioning. The DDL runs once.
- Do not over-engineer the query. A couple of JOINs returning readable text is fine.

## Exit Criteria

Two tests pass:

1. **MCP Inspector:** Call `lookup_item` with `"Headhunter"` and get back real mod data from the local SQLite database.

2. **Claude Desktop:** Ask in natural language: "What mods does Headhunter have?" and get an accurate answer sourced from the local database.

That is the only thing that matters.
