using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Aoe2DEOverlay;

public sealed class OverlayPlayerViewModel : INotifyPropertyChanged
{
    private string _winRate = "—";
    private string _record = "—";
    private string _oneVsOneRating = "—";
    private string _teamRating = "—";
    private IReadOnlyList<string> _recentCivilizations = Array.Empty<string>();

    public required string Name { get; init; }
    public int ProfileId { get; init; }
    public string WinRate { get => _winRate; private set => Set(ref _winRate, value); }
    public string Record { get => _record; private set => Set(ref _record, value); }
    public string OneVsOneRating { get => _oneVsOneRating; private set => Set(ref _oneVsOneRating, value); }
    public string TeamRating { get => _teamRating; private set => Set(ref _teamRating, value); }
    public IReadOnlyList<string> RecentCivilizations { get => _recentCivilizations; private set => Set(ref _recentCivilizations, value); }

    public void Apply(PlayerStatistics statistics)
    {
        WinRate = statistics.WinRate is { } winRate ? $"{winRate}%" : "—";
        Record = statistics.DisplayWins is { } wins && statistics.DisplayLosses is { } losses ? $"{wins}W  {losses}L" : "—";
        OneVsOneRating = statistics.OneVsOneRating?.ToString() ?? "—";
        TeamRating = statistics.TeamRating?.ToString() ?? "—";
        RecentCivilizations = statistics.RecentCivilizations;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
