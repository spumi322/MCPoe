using System.Text.Json;
using MCPoe.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace MCPoe.Infrastructure.PoB;

public sealed class PoBService : IPoBService
{
    private const string LuaJitPath = @"C:\Users\spumi\scoop\shims\luajit.exe";
    private const string PobSrcPath = @"G:\Code\utils\PathOfBuilding\src";

    private static readonly JsonSerializerOptions OutputJson = new() { WriteIndented = true };

    private readonly ILogger<PoBService> _logger;

    public PoBService(ILogger<PoBService> logger) => _logger = logger;

    public async Task<string> CalculateNewBuildAsync(CancellationToken ct)
    {
        await using var bridge = await PoBBridge.StartAsync(LuaJitPath, PobSrcPath, _logger, ct)
            .ConfigureAwait(false);

        using (var pong = await bridge.SendAsync("ping", null, ct).ConfigureAwait(false))
        {
            EnsureOk(pong, "ping");
        }

        using (var created = await bridge.SendAsync("new_build", null, ct).ConfigureAwait(false))
        {
            EnsureOk(created, "new_build");
        }

        using var info = await bridge.SendAsync("get_build_info", null, ct).ConfigureAwait(false);
        EnsureOk(info, "get_build_info");

        using var stats = await bridge.SendAsync("get_stats", null, ct).ConfigureAwait(false);
        EnsureOk(stats, "get_stats");

        var combined = new Dictionary<string, JsonElement>
        {
            ["build_info"] = info.RootElement.TryGetProperty("info", out var infoEl)
                ? infoEl.Clone()
                : info.RootElement.Clone(),
            ["stats"] = stats.RootElement.TryGetProperty("stats", out var statsEl)
                ? statsEl.Clone()
                : stats.RootElement.Clone(),
        };

        return JsonSerializer.Serialize(combined, OutputJson);
    }

    private static void EnsureOk(JsonDocument doc, string action)
    {
        var root = doc.RootElement;
        var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
        if (ok) return;

        var error = root.TryGetProperty("error", out var errEl) ? errEl.ToString() : root.GetRawText();
        throw new InvalidOperationException($"PoB action '{action}' failed: {error}");
    }
}
