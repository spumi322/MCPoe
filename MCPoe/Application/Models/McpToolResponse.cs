using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCPoe.Application.Models;

public sealed record McpToolResponse(
    string Status,
    bool Grounded,
    bool MustAnswerFromResults,
    string Instruction,
    string Tool,
    string Query,
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
        bool grounded,
        bool mustAnswerFromResults,
        string instruction,
        string tool,
        string query,
        object? metadata,
        object? results,
        McpToolError? error = null)
    {
        var response = new McpToolResponse(
            Status: status,
            Grounded: grounded,
            MustAnswerFromResults: mustAnswerFromResults,
            Instruction: instruction,
            Tool: tool,
            Query: query,
            Metadata: metadata ?? new { },
            Results: results ?? Array.Empty<object>(),
            Error: error);

        return JsonSerializer.Serialize(response, JsonOptions);
    }
}

public sealed record McpToolError(string Reason);
