namespace Aoe2DEOverlay;

public sealed record PlayerStatistics
{
    public int ProfileId { get; init; }
    public int? OneVsOneRating { get; init; }
    public int? OneVsOneWins { get; init; }
    public int? OneVsOneLosses { get; init; }
    public int? TeamRating { get; init; }
    public int? TeamWins { get; init; }
    public int? TeamLosses { get; init; }
    public IReadOnlyList<string> RecentCivilizations { get; init; } = Array.Empty<string>();

    public int? DisplayWins => HasOneVsOneHistory ? OneVsOneWins : TeamWins;
    public int? DisplayLosses => HasOneVsOneHistory ? OneVsOneLosses : TeamLosses;
    public bool HasOneVsOneHistory => (OneVsOneWins ?? 0) + (OneVsOneLosses ?? 0) > 0;
    public int? WinRate
    {
        get
        {
            var wins = DisplayWins;
            var losses = DisplayLosses;
            var total = wins + losses;
            return wins.HasValue && losses.HasValue && total > 0
                ? (int)Math.Round(wins.Value * 100d / total.Value)
                : null;
        }
    }
}
