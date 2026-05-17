using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MCPoe.Infrastructure.PoB;

public sealed class PoBBridge : IAsyncDisposable
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(2);

    private readonly Process _process;
    private readonly ILogger _logger;
    private readonly Task _stderrPump;
    private bool _disposed;

    private PoBBridge(Process process, ILogger logger, Task stderrPump)
    {
        _process = process;
        _logger = logger;
        _stderrPump = stderrPump;
    }

    public bool IsAlive => !_disposed && !_process.HasExited;

    public static async Task<PoBBridge> StartAsync(
        string luaJitPath,
        string pobSrcPath,
        ILogger logger,
        CancellationToken ct)
    {
        var trimmedSrc = pobSrcPath.TrimEnd('/', '\\');
        var baseDir = Path.GetDirectoryName(trimmedSrc)
            ?? throw new InvalidOperationException($"Cannot derive base dir from '{pobSrcPath}'");
        var baseDirFwd = baseDir.Replace('\\', '/');

        var luaPath = $"{baseDirFwd}/runtime/lua/?.lua;{baseDirFwd}/runtime/lua/?/init.lua;;";
        var luaCPath = $"{baseDirFwd}/runtime/?.dll;;";

        var psi = new ProcessStartInfo
        {
            FileName = luaJitPath,
            WorkingDirectory = pobSrcPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("HeadlessWrapper.lua");
        psi.Environment["POB_API_STDIO"] = "1";
        psi.Environment["LUA_PATH"] = luaPath;
        psi.Environment["LUA_CPATH"] = luaCPath;

        logger.LogInformation("Starting LuaJIT bridge: {Lua} cwd={Cwd}", luaJitPath, pobSrcPath);
        logger.LogDebug("LUA_PATH={LuaPath}", luaPath);
        logger.LogDebug("LUA_CPATH={LuaCPath}", luaCPath);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Process.Start returned null for LuaJIT");

        var stderrPump = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    logger.LogDebug("[lua stderr] {Line}", line);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "stderr pump ended");
            }
        });

        var bridge = new PoBBridge(process, logger, stderrPump);
        try
        {
            await bridge.WaitForReadyAsync(ct).ConfigureAwait(false);
            return bridge;
        }
        catch
        {
            await bridge.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task WaitForReadyAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ReadyTimeout);

        while (true)
        {
            var line = await ReadLineAsync(cts.Token).ConfigureAwait(false);
            if (line is null)
                throw new InvalidOperationException("LuaJIT process exited before ready signal");

            if (!TryParseJsonObject(line, out var doc))
                continue;

            using (doc)
            {
                if (doc.RootElement.TryGetProperty("ready", out var ready) &&
                    ready.ValueKind == JsonValueKind.True)
                {
                    _logger.LogInformation("LuaJIT bridge ready");
                    return;
                }
            }
        }
    }

    public async Task<JsonDocument> SendAsync(string action, object? @params, CancellationToken ct)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PoBBridge));
        if (_process.HasExited)
            throw new InvalidOperationException($"LuaJIT process exited before action '{action}'");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);

        var payload = new Dictionary<string, object?> { ["action"] = action };
        if (@params is not null) payload["params"] = @params;
        var json = JsonSerializer.Serialize(payload);

        try
        {
            _logger.LogDebug("[lua tx] {Json}", json);
            await _process.StandardInput.WriteLineAsync(json.AsMemory(), cts.Token).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cts.Token).ConfigureAwait(false);

            while (true)
            {
                if (_process.HasExited)
                    throw new InvalidOperationException($"LuaJIT process exited while awaiting action '{action}'");

                var line = await ReadLineAsync(cts.Token).ConfigureAwait(false);
                if (line is null)
                    throw new InvalidOperationException("LuaJIT process exited while awaiting response");

                if (TryParseJsonObject(line, out var doc))
                    return doc;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"PoB action '{action}' timed out after {RequestTimeout.TotalSeconds:0} seconds.");
        }
    }

    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var line = await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
        if (line is null) return null;
        line = line.TrimEnd('\r');
        _logger.LogDebug("[lua stdout] {Line}", line);
        return line;
    }

    private static bool TryParseJsonObject(string line, out JsonDocument doc)
    {
        doc = null!;
        var span = line.AsSpan().Trim();
        if (span.Length == 0 || span[0] != '{')
            return false;
        try
        {
            doc = JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    await _process.StandardInput.WriteAsync("{\"action\":\"quit\"}\n").ConfigureAwait(false);
                    await _process.StandardInput.FlushAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to send quit");
                }

                try { _process.StandardInput.Close(); } catch { }

                if (!_process.WaitForExit(checked((int)QuitTimeout.TotalMilliseconds)))
                {
                    _logger.LogWarning("LuaJIT did not exit after quit; killing");
                    try { _process.Kill(entireProcessTree: true); } catch (Exception ex) { _logger.LogDebug(ex, "Kill failed"); }
                }
            }
        }
        finally
        {
            try { await _stderrPump.ConfigureAwait(false); } catch { }
            _process.Dispose();
        }
    }
}
