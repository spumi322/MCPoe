using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MCPoe.Tools;

[McpServerToolType]
public sealed class EchoTool
{
    [McpServerTool(Name = "echo")]
    [Description("Echoes back the input message. Used for testing MCP connectivity.")]
    public static Task<string> EchoAsync(
        [Description("The message to echo back.")] string message)
        => Task.FromResult($"[MCPoe] {message}");
}
