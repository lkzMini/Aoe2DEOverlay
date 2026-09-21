using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

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
    public int Slot { get; init; }
    public int Color { get; init; }
    public int Team { get; init; }
    public bool ShowTeamSeparator { get; set; }
    public Brush SlotBadgeBrush => Color switch
    {
        1 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(54, 122, 219)),
        2 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 76, 76)),
        3 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 166, 91)),
        4 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 196, 66)),
        5 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(57, 190, 199)),
        6 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(164, 99, 199)),
        7 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(151, 158, 168)),
        8 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(222, 142, 68)),
        _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(91, 98, 108))
    };
    public Brush SlotBadgeForeground => Color is 4 or 5 or 7 or 8 ? Brushes.Black : Brushes.White;
    public string WinRate { get => _winRate; private set => Set(ref _winRate, value); }
    public string Record { get => _record; private set => Set(ref _record, value); }
    public string OneVsOneRating { get => _oneVsOneRating; private set => Set(ref _oneVsOneRating, value); }
    public string TeamRating { get => _teamRating; private set => Set(ref _teamRating, value); }
    public IReadOnlyList<string> RecentCivilizations { get => _recentCivilizations; private set => Set(ref _recentCivilizations, value); }
    public bool HasRecentCivilizations => RecentCivilizations.Count > 0;

    public void Apply(PlayerStatistics statistics)
    {
        WinRate = statistics.WinRate is { } winRate ? $"{winRate}%" : "—";
        Record = statistics.DisplayWins is { } wins && statistics.DisplayLosses is { } losses ? $"{wins}W  {losses}L" : "—";
        OneVsOneRating = statistics.OneVsOneRating?.ToString() ?? "—";
        TeamRating = statistics.TeamRating?.ToString() ?? "—";
        RecentCivilizations = statistics.RecentCivilizations;
        OnPropertyChanged(nameof(HasRecentCivilizations));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
