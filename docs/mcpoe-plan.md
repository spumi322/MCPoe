# MCPoe -- Implementation Plan

A self-hosted C# MCP server for Path of Exile 1 character build analysis. Stdio transport, local-only, learning/portfolio project.

---

## Overview

**Three MCP tools bundled in one server:**

- **Tool 1 (Wiki RAG):** Semantic search over 13,290 pre-scraped poewiki.net markdown files (~40MB). Answers general PoE mechanic and knowledge questions.
- **Tool 2 (Mods/Stats DB):** Structured SQLite database of item mods, stats, skill levels, etc. Bulk-exported from poewiki.net Cargo API. Answers precise data queries.
- **Tool 3 (Headless PoB):** C# subprocess wrapper around a forked Path of Building calculation engine (Lua/LuaJIT). Accepts build codes, returns calculated stats (DPS, defenses, etc.).

**Tech stack:**

- C# / .NET 10
- `ModelContextProtocol` NuGet package (official C# MCP SDK, v1.0)
- Stdio transport (Claude Desktop launches the .exe)
- SQLite (two databases: vectors.db, mods.db)
- sqlite-vec (vector search extension)
- Voyage AI (embeddings)
- LuaJIT + forked PathOfBuilding (headless calculation engine)
- Serilog (structured logging)
- xUnit + Moq (testing)

**Architecture:** Single project with namespace separation. Application (interfaces, contracts), Infrastructure (Wiki, ModsDb, PoB), Models, Tools, Program.cs. Test project separate.

---

## P-1: Dependency Validation

**Goal:** Confirm the PoB fork works before committing to the project. Pass/fail gate.

- [ ] Install LuaJIT on Windows
- [ ] Clone `ianderse/PathOfBuilding`, checkout `api-stdio`
- [ ] Clone `ianderse/pob-mcp`, install, build
- [ ] Configure Claude Desktop to use pob-mcp as MCP server
- [ ] Test: load a build, read stats, modify a node, recalculate
- [ ] Document the stdin/stdout protocol (commands, response formats)
- [ ] Fork `ianderse/PathOfBuilding` to own GitHub

**Exit criteria:** PoB fork runs stable, calculations return correct data, protocol understood and documented.

---

## P0: Project Setup & Architecture

**Goal:** Solid foundation. Everything after this is adding tools to a working chassis.

- [ ] Create `MCPoe` C# solution targeting .NET 10
- [ ] Single project with namespace separation:
  - `Application/` -- interfaces, tool handler contracts
  - `Infrastructure/Wiki/` -- Tool 1 implementations
  - `Infrastructure/ModsDb/` -- Tool 2 implementations
  - `Infrastructure/PoB/` -- Tool 3 implementations
  - `Models/` -- shared DTOs, request/response types
  - `Tools/` -- MCP tool registrations, descriptions
  - `Program.cs` -- bootstrap, DI, stdio transport
- [ ] Add `ModelContextProtocol` NuGet package, configure stdio transport
- [ ] Serilog with structured logging
- [ ] Global error handling: tool calls return structured errors to the LLM, never crash the server
- [ ] Interface-driven DI: abstractions in Application, implementations in Infrastructure
- [ ] SQLite setup: two database files (vectors.db, mods.db), connection management, base repository pattern
- [ ] Test project: `MCPoe.Tests` with xUnit + Moq
- [ ] One dummy tool to verify end-to-end MCP handshake with Claude Desktop

**Exit criteria:** Solution builds, tests pass, Claude Desktop discovers MCPoe, calls the dummy tool, error handling catches and logs a deliberately thrown exception gracefully.

---

## P1: Proof of Concept

**Goal:** All three tools working end-to-end with real data in minimal form. One prompt exercises all three.

### P1a: Tool 3 -- Headless PoB (minimal)

- [ ] C# `Process.Start()` wrapper that launches LuaJIT with the PoB fork
- [ ] Implement minimum protocol: load build, get stats, read response
- [ ] Parse stdout response into structured result
- [ ] Register as MCP tool: accepts a PoB build code, returns basic stats (DPS, life, ES)
- [ ] Test: paste a build code to Claude, get real calculated numbers back

### P1b: Tool 2 -- Mods DB (minimal)

- [ ] C# script/command that calls Cargo API for one table (e.g., unique items), dumps into mods.db
- [ ] One MCP tool: `lookup_item` -- takes item name, queries SQLite, returns mods/stats
- [ ] Test: ask Claude about a unique item, get real mod data back

### P1c: Tool 1 -- Wiki RAG (minimal)

- [ ] Load 50-100 scraped markdown files into memory at startup
- [ ] Naive keyword search, no embeddings
- [ ] One MCP tool: `search_wiki` -- takes query, returns best matching content
- [ ] Test: ask Claude a general PoE mechanic question, get wiki content back

**Exit criteria:** Single prompt -- "use MCPoe. What does Righteous Fire do? What mods can roll on a Voidforge? Calculate the DPS of [build code]" -- all three tools called, all three return real data.

---

## P2: Tool 3 -- Headless PoB (full)

**Goal:** Complete PoB integration with robust lifecycle management.

- [ ] Complete protocol coverage: load, save, modify tree, swap gear, recalculate, compare snapshots
- [ ] Robust subprocess lifecycle: start on boot, restart on crash, timeout handling
- [ ] Multiple focused MCP tools (not one mega-tool, several the LLM picks from)
- [ ] Structured error responses when PoB returns invalid data

---

## P3: Tool 2 -- Mods DB (full)

**Goal:** Comprehensive item/mod database with rich query tools.

- [ ] Full Cargo API export script covering all relevant tables (items, mods, skill_levels, passive skills, etc.)
- [ ] Schema design in SQLite matching Cargo table relationships
- [ ] Multiple MCP tools: search items by mod, lookup skill gem stats, query passive nodes, filter by item class
- [ ] Pagination/filtering for large result sets

---

## P4: Tool 1 -- Wiki RAG (full)

**Goal:** Production-quality semantic search over the entire PoE wiki.

- [ ] New chunking strategy designed for wiki markdown structure
- [ ] Voyage AI embeddings for all 13K files
- [ ] sqlite-vec for indexed vector search
- [ ] Embedding cache (content-hash based, avoid re-embedding unchanged content)
- [ ] Search tuning: TopK, MinScore calibrated for PoE queries
- [ ] Tool descriptions tuned so the LLM knows when to use wiki vs mods DB

---

## Key Dependencies

| Dependency | Purpose | Risk |
|---|---|---|
| `ianderse/PathOfBuilding` (api-stdio) | Headless PoB calculation engine | Solo maintainer, unmerged PR, fork mitigates |
| `ModelContextProtocol` NuGet | C# MCP SDK | v1.0, Microsoft-maintained, low risk |
| Voyage AI | Embedding generation | Paid API, replaceable |
| poewiki.net Cargo API | Mod/stat data export | Community-maintained, no SLA, local DB mitigates |
| sqlite-vec | Vector search in SQLite | Community extension, low risk |
| LuaJIT | Runs PoB Lua code | Stable, mature, low risk |

## Key Decisions

- **Two separate SQLite databases:** vectors.db for wiki embeddings, mods.db for structured data
- **LLM orchestrates tool calls:** tools are independent, LLM decides which to call and in what order
- **Stale data is acceptable:** wiki and mod data refreshed manually, not live-synced
- **Stdio transport:** simplest self-hosting, no web server, no auth. Upgradeable to SSE/HTTP later without architectural changes
