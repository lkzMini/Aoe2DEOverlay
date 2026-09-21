using System.Collections.ObjectModel;
using System.Windows;

namespace Aoe2DEOverlay;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<OverlayPlayerViewModel> _players = new();
    private readonly WatchRecordService _watcher = new();
    private readonly PlayerStatsService _stats = new();
    private CancellationTokenSource? _statsCancellation;

    public MainWindow()
    {
        InitializeComponent();
        PlayersList.ItemsSource = _players;
        _watcher.StateChanged += SetStatus;
        _watcher.MatchDetected += MatchDetected;
        Loaded += (_, _) => Start();
        Closed += (_, _) => DisposeServices();
    }

    private void Start()
    {
        if (Environment.GetCommandLineArgs().Contains("--mock", StringComparer.OrdinalIgnoreCase))
        {
            ShowMockData();
            return;
        }
        _watcher.Start();
    }

    private void MatchDetected(Match match)
    {
        Dispatcher.Invoke(() =>
        {
            _players.Clear();
            foreach (var player in match.Players.Where(player => !player.IsAi))
                _players.Add(new OverlayPlayerViewModel { Name = player.Name, ProfileId = player.Id });
            StatusText.Visibility = Visibility.Collapsed;
        });

        _statsCancellation?.Cancel();
        _statsCancellation?.Dispose();
        _statsCancellation = new CancellationTokenSource();
        _ = LoadStatsAsync(match, _statsCancellation.Token);
    }

    private async Task LoadStatsAsync(Match match, CancellationToken cancellationToken)
    {
        try
        {
            var stats = await _stats.GetAsync(match.Players, cancellationToken: cancellationToken);
            await Dispatcher.InvokeAsync(() =>
            {
                foreach (var player in _players)
                    if (stats.TryGetValue(player.ProfileId, out var playerStats)) player.Apply(playerStats);

                if (_players.Count > 0 && stats.Values.All(value => value.OneVsOneRating is null && value.TeamRating is null))
                {
                    StatusText.Text = "Stats unavailable · replay data shown";
                    StatusText.Visibility = Visibility.Visible;
                }
            });
        }
        catch (OperationCanceledException) { }
    }

    private void SetStatus(string status) => Dispatcher.Invoke(() =>
    {
        StatusText.Text = status;
        StatusText.Visibility = Visibility.Visible;
        if (status.StartsWith("Reading", StringComparison.Ordinal)) _players.Clear();
    });

    private void ShowMockData()
    {
        _players.Clear();
        var samples = new[]
        {
            ("Hera", 2612, 1548, 124, 61, new[] { "MAY", "MON", "HUN", "VIK", "CHI" }),
            ("Liereyy", 2580, 1602, 91, 58, new[] { "ETH", "MAL", "MON", "HIN", "FRA" })
        };
        foreach (var sample in samples)
        {
            var player = new OverlayPlayerViewModel { Name = sample.Item1, ProfileId = 1 };
            player.Apply(new PlayerStatistics
            {
                ProfileId = 1, OneVsOneRating = sample.Item2, TeamRating = sample.Item3,
                OneVsOneWins = sample.Item4, OneVsOneLosses = sample.Item5, RecentCivilizations = sample.Item6
            });
            _players.Add(player);
        }
        StatusText.Visibility = Visibility.Collapsed;
    }

    private void DisposeServices()
    {
        _statsCancellation?.Cancel();
        _statsCancellation?.Dispose();
        _watcher.Dispose();
        _stats.Dispose();
    }
}
