using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCPoe.Application.Models;

public sealed record McpToolResponse(
    string Status,
    string Tool,
    object Metadata,
    object Results,
    McpToolError? Error)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(
        string status,
        string tool,
        object? metadata,
        object? results,
        McpToolError? error = null)
    {
        var response = new McpToolResponse(
            Status: status,
            Tool: tool,
            Metadata: metadata ?? new { },
            Results: results ?? Array.Empty<object>(),
            Error: error);

        return JsonSerializer.Serialize(response, JsonOptions);
    }
}

public sealed record McpToolError(string Reason);
