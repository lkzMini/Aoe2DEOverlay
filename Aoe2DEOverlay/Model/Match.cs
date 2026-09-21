namespace Aoe2DEOverlay;

public sealed class Match
{
    public string ReplayPath { get; init; } = "";
    public string Identity { get; init; } = "";
    public uint Started { get; init; }
    public bool IsMultiplayer { get; init; }
    public IReadOnlyList<Player> Players { get; init; } = Array.Empty<Player>();
}
