# MCPoe -- P1a: Headless PoB Bridge (Proof of Concept)

> Historical POC note. This document is retained as bridge background, not as the current PoB tool plan. For current PoB agent-substrate work, start with `docs/pob_agent_overview.md`.

## Context

MCPoe is a C# MCP server (.NET 10, stdio transport) for Path of Exile build analysis. P0 is complete: the solution builds, has Serilog file logging, interface-driven DI, and stub tools including `calculate_build`.

P1a is a **proof of concept**. The only goal is to validate that C# can spawn the LuaJIT process, communicate with it, and return real calculated data through an MCP tool. Keep it minimal. No polish, no edge cases, no production hardening. That comes later.

## Architecture

```
Claude Desktop ←(MCP stdio)→ MCPoe (C#) ←(JSON stdin/stdout)→ LuaJIT subprocess
```

The MCP layer (left side) is already handled. You are implementing the right side.

## The PoB Headless Protocol

Custom JSON envelope over newline-delimited stdio. NOT JSON-RPC.

**Request** (write to LuaJIT stdin):
```json
{"action": "get_stats", "params": {"fields": ["dps"]}}\n
```

**Response** (read from LuaJIT stdout):
```json
{"ok": true, "stats": {...}}\n
```

**Startup sequence -- CRITICAL:**
1. Spawn `luajit HeadlessWrapper.lua` with env var `POB_API_STDIO=1`
2. The process outputs **noise on stdout** -- log messages, "LOADING" text, non-JSON lines
3. Read and discard lines until you find `{"ready": true}`
4. After that, the bridge accepts requests

**Communication rules:**
- One request at a time (no concurrency support in the Lua bridge)
- Responses also have noise lines mixed in -- skip non-JSON, take first valid JSON
- Windows may emit `\r\n` -- handle it
- Send `{"action": "quit"}` to shut down gracefully

**Required env vars for the subprocess:**
- `POB_API_STDIO=1`
- `LUA_PATH` and `LUA_CPATH` -- derived from the fork path:
  - Fork path is `.../PathOfBuilding/src`, strip `/src` to get `baseDir`
  - `LUA_PATH` = `{baseDir}/runtime/lua/?.lua;{baseDir}/runtime/lua/?/init.lua;;`
  - `LUA_CPATH` = `{baseDir}/runtime/?.dll;;`

## What to Build

Wire the existing `calculate_build` stub tool to a real LuaJIT subprocess. For POC, the tool creates a new build via the engine and returns its base stats. Just prove the C#-to-Lua communication works end-to-end.

**Actions needed for POC (nothing more):** `ping`, `new_build`, `get_stats`, `get_build_info`, `quit`

**Hardcoded paths are fine for POC:**
- LuaJIT: `C:\Users\spumi\scoop\shims\luajit.exe`
- PoB fork: `G:\Code\utils\PathOfBuilding\src`

**No build code decoding.** For POC, use `new_build` to create a fresh build (e.g. a Witch Occultist) and read its stats. That is enough to prove the bridge works. Build code import is a later feature.

## Guidelines

- Use `System.Text.Json`, not Newtonsoft
- Do NOT write to stdout -- it is the MCP transport. Serilog file sink only.
- Do NOT let exceptions crash the MCP server. Tool calls return error strings on failure.
- Keep it simple. This is a POC. Hardcoded values, minimal error handling, no config binding, no restart logic. Just make it work end-to-end.

## Exit Criteria

In Claude Desktop:
```
Use mcpoe. Create a new Witch build and show me the base stats.
```
Returns real stats from the PoB engine. That is the only test that matters.
