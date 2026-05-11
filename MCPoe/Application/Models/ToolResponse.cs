namespace MCPoe.Application.Models;

public sealed record ToolResponse(bool Success, string Content, string? Error = null)
{
    public static ToolResponse Ok(string content) => new(true, content);
    public static ToolResponse Fail(string error) => new(false, string.Empty, error);
}
