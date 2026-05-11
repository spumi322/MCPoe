# MCPoe -- P0 Project Setup

You are scaffolding a C# MCP server project called **MCPoe**. This is a self-hosted MCP server for Path of Exile character build analysis. It will serve tools to an LLM via stdio transport.

## Tech Stack
- .NET 10 console application
- ModelContextProtocol v1.3.0 NuGet (stdio transport, NOT AspNetCore)
- Microsoft.Extensions.Hosting
- Serilog + Serilog.Sinks.File (structured logging to file, NOT console -- stdout is reserved for MCP protocol)
- Microsoft.Data.Sqlite
- xUnit for tests

## Solution Structure

Create a solution `MCPoe.sln` with two projects:
1. `MCPoe` -- console app (the MCP server)
2. `MCPoe.Tests` -- xUnit test project referencing MCPoe

## MCPoe Project Structure

```
MCPoe/
├── Application/
│   ├── Interfaces/
│   │   ├── IWikiSearchService.cs
│   │   ├── IModsDbService.cs
│   │   └── IPoBService.cs
│   └── Models/
│       └── ToolResponse.cs
├── Infrastructure/
│   ├── Wiki/
│   │   └── WikiSearchService.cs      (stub, implements IWikiSearchService)
│   ├── ModsDb/
│   │   └── ModsDbService.cs          (stub, implements IModsDbService)
│   ├── PoB/
│   │   └── PoBService.cs             (stub, implements IPoBService)
│   └── Database/
│       └── DatabaseInitializer.cs    (creates SQLite files if not exist)
├── Tools/
│   ├── EchoTool.cs                   (dummy tool for MCP handshake validation)
│   ├── WikiSearchTool.cs             (stub, not implemented yet)
│   ├── ModsSearchTool.cs             (stub, not implemented yet)
│   └── PoBCalculateTool.cs           (stub, not implemented yet)
├── Program.cs
└── appsettings.json
```

## Program.cs Requirements

- Use `Microsoft.Extensions.Hosting` generic host builder
- Configure MCP server with stdio transport using the ModelContextProtocol SDK
- Register all tools using the SDK's tool discovery (attribute-based: `[McpServerToolType]` and `[McpServerTool]`)
- Register services in DI: interfaces to stub implementations
- Configure Serilog to log to a file (`logs/mcpoe-.log`, rolling daily). Do NOT log to console/stdout -- that channel is the MCP protocol transport
- Global exception handling: catch unhandled exceptions, log them, do not crash the server process

## EchoTool.cs

This is the validation tool. Implement it fully:
- Decorated with `[McpServerToolType]` and `[McpServerTool]`
- Name: `echo`
- Description: `"Echoes back the input message. Used for testing MCP connectivity."`
- Takes a single string parameter `message`
- Returns the message prefixed with `"[MCPoe] "`

## Stub Tools (WikiSearchTool, ModsSearchTool, PoBCalculateTool)

Each stub tool should:
- Have correct MCP attributes and descriptive names/descriptions
- Accept reasonable placeholder parameters
- Return a string saying `"Not implemented yet. This tool will be available in P1."`
- Inject the corresponding interface via constructor

Tool details:
- `search_wiki`: takes `query` (string), description: `"Search the Path of Exile wiki for game mechanic information."`
- `search_mods`: takes `query` (string) and optional `item_class` (string), description: `"Search the item mods database for mod stats and item properties."`
- `calculate_build`: takes `build_code` (string), description: `"Calculate DPS and stats for a Path of Exile build using the Path of Building engine."`

## Interfaces

Keep them minimal for now:
- `IWikiSearchService`: `Task<string> SearchAsync(string query)`
- `IModsDbService`: `Task<string> SearchModsAsync(string query, string? itemClass = null)`
- `IPoBService`: `Task<string> CalculateBuildAsync(string buildCode)`

## Stub Implementations

Each implementation should just return `"Not implemented"` for now. They will be replaced in P1-P4.

## DatabaseInitializer.cs

- On startup, ensure two SQLite database files exist: `data/vectors.db` and `data/mods.db`
- Create the `data/` directory if it doesn't exist
- Do NOT create any tables yet -- just the empty files
- Log the database paths on initialization

## appsettings.json

```json
{
  "Database": {
    "VectorsPath": "data/vectors.db",
    "ModsPath": "data/mods.db"
  },
  "Logging": {
    "LogPath": "logs/mcpoe-.log"
  }
}
```

## Tests (MCPoe.Tests)

Write a basic test:
- `EchoToolTests.cs`: verify the echo tool returns the expected prefixed message

## Important Constraints

- Do NOT use ModelContextProtocol.AspNetCore. This is a stdio server, not HTTP.
- Do NOT write to stdout for logging. Stdout is the MCP transport channel. Use Serilog file sink only.
- All tool methods must be async and handle exceptions gracefully, returning error messages to the LLM rather than throwing.
- Use nullable reference types throughout.
- Target `net10.0`.

## Exit Criteria

After running this project and configuring it in Claude Desktop, the `echo` tool should be discoverable and callable. The three stub tools should appear in the tool list with their descriptions. The server should not crash on any input.
