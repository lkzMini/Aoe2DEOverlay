using System.Collections.ObjectModel;

namespace Aoe2DEOverlay;

/// <summary>Compact presentation group built from the replay's authoritative team value.</summary>
public sealed class OverlayTeamViewModel
{
    public OverlayTeamViewModel(int team, bool hasTopSpacing)
    {
        Team = team;
        HasTopSpacing = hasTopSpacing;
    }

    public int Team { get; }
    public bool HasTopSpacing { get; }
    public string Label => Team > 0 ? $"TEAM {Team}" : "PLAYERS";
    public ObservableCollection<OverlayPlayerViewModel> Players { get; } = new();
    public string PlayerCountLabel => $"{Players.Count} {(Players.Count == 1 ? "player" : "players")}";
}
