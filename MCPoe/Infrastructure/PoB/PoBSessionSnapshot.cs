namespace MCPoe.Infrastructure.PoB;

public sealed record PoBSessionSnapshot(
    bool EngineAlive,
    bool HasLoadedBuild,
    string? BuildName,
    string? LastAction,
    DateTimeOffset? EngineStartedUtc,
    bool StateLostOnLastRestart);

