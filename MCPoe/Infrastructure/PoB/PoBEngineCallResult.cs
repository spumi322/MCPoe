using System.Text.Json;

namespace MCPoe.Infrastructure.PoB;

public sealed class PoBEngineCallResult : IDisposable
{
    public PoBEngineCallResult(JsonDocument response, PoBSessionSnapshot session)
    {
        Response = response;
        Session = session;
    }

    public JsonDocument Response { get; }

    public PoBSessionSnapshot Session { get; }

    public void Dispose() => Response.Dispose();
}

