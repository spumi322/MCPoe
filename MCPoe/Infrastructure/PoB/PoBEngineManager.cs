using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MCPoe.Infrastructure.PoB;

public sealed class PoBEngineManager : IAsyncDisposable
{
    private readonly ILogger<PoBEngineManager> _logger;
    private readonly string _luaJitPath;
    private readonly string _pobSrcPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private PoBBridge? _bridge;
    private bool _hasLoadedBuild;
    private string? _buildName;
    private string? _lastAction;
    private DateTimeOffset? _engineStartedUtc;
    private bool _stateLostOnLastRestart;
    private bool _disposed;

    public PoBEngineManager(ILogger<PoBEngineManager> logger, IConfiguration configuration)
    {
        _logger = logger;
        _luaJitPath = RequiredConfig(configuration, "PoB:LuaJitPath");
        _pobSrcPath = RequiredConfig(configuration, "PoB:SourcePath");
    }

    public PoBSessionSnapshot Snapshot => CreateSnapshot();

    public async Task<PoBEngineCallResult> ExecuteAsync(string action, object? parameters, CancellationToken ct)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PoBEngineManager));
        if (IsBlockedAction(action))
            throw new InvalidOperationException($"PoB action '{action}' is intentionally blocked in this phase.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureStartedAsync(ct).ConfigureAwait(false);

            if (RequiresLoadedBuild(action) && !_hasLoadedBuild)
            {
                var reason = _stateLostOnLastRestart
                    ? "PoB engine restarted and the previous in-memory build state was lost. Call pob_new_build or pob_load_build_xml first."
                    : "No PoB build is loaded. Call pob_new_build or pob_load_build_xml first.";
                throw new InvalidOperationException(reason);
            }

            try
            {
                var response = await _bridge!.SendAsync(action, parameters, ct).ConfigureAwait(false);
                _lastAction = action;

                if (ResponseOk(response))
                    ApplySuccessfulAction(action, parameters, response.RootElement);

                return new PoBEngineCallResult(response, CreateSnapshot());
            }
            catch
            {
                await MarkEngineDeadAsync(stateLost: _hasLoadedBuild).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureStartedAsync(CancellationToken ct)
    {
        if (_bridge is { IsAlive: true })
            return;

        if (_bridge is not null)
            await MarkEngineDeadAsync(stateLost: _hasLoadedBuild).ConfigureAwait(false);

        ValidateConfiguration();

        _logger.LogInformation("Starting process-scoped PoB engine");
        _bridge = await PoBBridge.StartAsync(_luaJitPath, _pobSrcPath, _logger, ct).ConfigureAwait(false);
        _engineStartedUtc = DateTimeOffset.UtcNow;
    }

    private async Task MarkEngineDeadAsync(bool stateLost)
    {
        var oldBridge = _bridge;
        _bridge = null;

        if (oldBridge is not null)
            await oldBridge.DisposeAsync().ConfigureAwait(false);

        _hasLoadedBuild = false;
        _buildName = null;
        _engineStartedUtc = null;
        _stateLostOnLastRestart = stateLost || _stateLostOnLastRestart;
    }

    private void ApplySuccessfulAction(string action, object? parameters, JsonElement response)
    {
        if (action == "new_build")
        {
            _hasLoadedBuild = true;
            _buildName = null;
            _stateLostOnLastRestart = false;
            return;
        }

        if (action == "load_build_xml")
        {
            _hasLoadedBuild = true;
            _buildName = TryGetName(parameters) ?? "API Build";
            _stateLostOnLastRestart = false;
            return;
        }

    }

    private PoBSessionSnapshot CreateSnapshot() =>
        new(
            EngineAlive: _bridge is { IsAlive: true },
            HasLoadedBuild: _hasLoadedBuild,
            BuildName: _buildName,
            LastAction: _lastAction,
            EngineStartedUtc: _engineStartedUtc,
            StateLostOnLastRestart: _stateLostOnLastRestart);

    private static bool ResponseOk(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;

    private static bool RequiresLoadedBuild(string action) =>
        action is "export_build_xml";

    private static bool IsBlockedAction(string action) => action == "calc_with";

    private static string RequiredConfig(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Missing required configuration: {key}");

        return value;
    }

    private void ValidateConfiguration()
    {
        if (!File.Exists(_luaJitPath))
            throw new InvalidOperationException($"PoB LuaJIT path does not exist: {_luaJitPath}");

        var wrapperPath = Path.Combine(_pobSrcPath, "HeadlessWrapper.lua");
        if (!File.Exists(wrapperPath))
            throw new InvalidOperationException($"PoB source path is invalid; HeadlessWrapper.lua was not found at: {wrapperPath}");
    }

    private static string? TryGetName(object? parameters)
    {
        if (parameters is null)
            return null;

        var element = JsonSerializer.SerializeToElement(parameters);
        return element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_bridge is not null)
                await _bridge.DisposeAsync().ConfigureAwait(false);

            _bridge = null;
            _hasLoadedBuild = false;
            _buildName = null;
            _engineStartedUtc = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
