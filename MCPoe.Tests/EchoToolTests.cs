using MCPoe.Tools;

namespace MCPoe.Tests;

public class EchoToolTests
{
    [Fact]
    public async Task EchoAsync_returns_message_with_prefix()
    {
        var result = await EchoTool.EchoAsync("hello");
        Assert.Equal("[MCPoe] hello", result);
    }

    [Fact]
    public async Task EchoAsync_preserves_empty_message()
    {
        var result = await EchoTool.EchoAsync(string.Empty);
        Assert.Equal("[MCPoe] ", result);
    }
}
