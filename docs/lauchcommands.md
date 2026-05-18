dotnet publish /g/Code/MCPoe/MCPoe/MCPoe.csproj -c Release -r win-x64 --self-contained true -o /g/Code/MCPoe/dist
npx @modelcontextprotocol/inspector /g/Code/MCPoe/dist/MCPoe.exe