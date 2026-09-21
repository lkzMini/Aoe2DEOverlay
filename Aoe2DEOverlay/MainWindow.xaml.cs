using System.Runtime.InteropServices;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Aoe2DEOverlay;

public partial class MainWindow : Window
{
    private const int ToggleLockHotkey = 1;
    private const int ToggleVisibilityHotkey = 2;
    private const int RefreshHotkey = 3;
    private const int QuitHotkey = 4;
    private readonly ObservableCollection<OverlayPlayerViewModel> _players = new();
    private readonly WatchRecordService _watcher = new();
    private readonly PlayerStatsService _stats = new();
    private readonly SettingsService _settingsService = new();
    private readonly OverlaySettings _settings;
    private CancellationTokenSource? _statsCancellation;
    private HwndSource? _windowSource;
    private nint _windowHandle;
    private bool _forceStatsRefresh;
    private bool _mockMode;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        ApplySavedSettings();
        PlayersList.ItemsSource = _players;
        _watcher.StateChanged += SetStatus;
        _watcher.MatchDetected += MatchDetected;
        Loaded += (_, _) => Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _windowSource?.AddHook(WindowMessageHook);
        ApplyOverlayWindowStyle();
        RegisterGlobalHotkeys();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SaveSettings();
        UnregisterGlobalHotkeys();
        _windowSource?.RemoveHook(WindowMessageHook);
        _statsCancellation?.Cancel();
        _statsCancellation?.Dispose();
        _watcher.Dispose();
        _stats.Dispose();
        base.OnClosing(e);
    }

    private void Start()
    {
        _mockMode = Environment.GetCommandLineArgs().Contains("--mock", StringComparer.OrdinalIgnoreCase);
        if (_settings.Hidden && !_mockMode) Hide();
        if (_mockMode)
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
            int? previousTeam = null;
            foreach (var player in match.Players.Where(player => !player.IsAi).OrderBy(player => player.Team).ThenBy(player => player.Slot))
            {
                var viewModel = new OverlayPlayerViewModel
                {
                    Name = player.Name,
                    ProfileId = player.Id,
                    Slot = player.Slot,
                    Color = player.Color,
                    Team = player.Team,
                    ShowTeamSeparator = previousTeam is not null && previousTeam != player.Team
                };
                _players.Add(viewModel);
                previousTeam = player.Team;
            }
            StatusText.Visibility = Visibility.Collapsed;
        });

        _statsCancellation?.Cancel();
        _statsCancellation?.Dispose();
        _statsCancellation = new CancellationTokenSource();
        var forceRefresh = _forceStatsRefresh;
        _forceStatsRefresh = false;
        _ = LoadStatsAsync(match, forceRefresh, _statsCancellation.Token);
    }

    private async Task LoadStatsAsync(Match match, bool forceRefresh, CancellationToken cancellationToken)
    {
        try
        {
            var stats = await _stats.GetAsync(match.Players, forceRefresh, cancellationToken);
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
        catch (Exception exception)
        {
            AppLogger.Error("Unexpected player stats failure", exception);
            await Dispatcher.InvokeAsync(() =>
            {
                if (_players.Count == 0) return;
                StatusText.Text = "Stats unavailable · replay data shown";
                StatusText.Visibility = Visibility.Visible;
            });
        }
    }

    private void SetStatus(string status) => Dispatcher.Invoke(() =>
    {
        StatusText.Text = status;
        StatusText.Visibility = Visibility.Visible;
        if (status.StartsWith("Reading", StringComparison.Ordinal)) _players.Clear();
    });

    private void ApplySavedSettings()
    {
        Opacity = Math.Clamp(_settings.Opacity, 0.35, 1);
        Left = IsVisibleCoordinate(_settings.X, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenWidth) ? _settings.X : 24;
        Top = IsVisibleCoordinate(_settings.Y, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenHeight) ? _settings.Y : 24;
        UpdateUnlockedControls();
    }

    private static bool IsVisibleCoordinate(double value, double origin, double length) => !double.IsNaN(value) && value >= origin - 100 && value <= origin + length - 40;

    private void ApplyOverlayWindowStyle()
    {
        if (_windowHandle == 0) return;
        var style = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle);
        style |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
        style = _settings.Locked ? style | NativeMethods.WsExTransparent : style & ~NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle, style);
        UpdateUnlockedControls();
    }

    private void UpdateUnlockedControls()
    {
        UnlockedIndicator.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;
        CloseButton.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RegisterGlobalHotkeys()
    {
        RegisterHotkey(ToggleLockHotkey, 0x4F, "Ctrl+Shift+O");
        RegisterHotkey(ToggleVisibilityHotkey, 0x48, "Ctrl+Shift+H");
        RegisterHotkey(RefreshHotkey, 0x52, "Ctrl+Shift+R");
        RegisterHotkey(QuitHotkey, 0x51, "Ctrl+Shift+Q");
    }

    private void RegisterHotkey(int id, uint virtualKey, string label)
    {
        if (!NativeMethods.RegisterHotKey(_windowHandle, id, NativeMethods.ModControl | NativeMethods.ModShift, virtualKey))
            AppLogger.Warning($"Global hotkey registration failed: {label}; Win32 error {Marshal.GetLastWin32Error()}");
    }

    private void UnregisterGlobalHotkeys()
    {
        if (_windowHandle == 0) return;
        NativeMethods.UnregisterHotKey(_windowHandle, ToggleLockHotkey);
        NativeMethods.UnregisterHotKey(_windowHandle, ToggleVisibilityHotkey);
        NativeMethods.UnregisterHotKey(_windowHandle, RefreshHotkey);
        NativeMethods.UnregisterHotKey(_windowHandle, QuitHotkey);
    }

    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != NativeMethods.WmHotkey) return 0;
        handled = true;
        switch (wParam.ToInt32())
        {
            case ToggleLockHotkey: ToggleLock(); break;
            case ToggleVisibilityHotkey: ToggleVisibility(); break;
            case RefreshHotkey: RefreshOverlay(); break;
            case QuitHotkey: Close(); break;
        }
        return 0;
    }

    private void ToggleLock()
    {
        _settings.Locked = !_settings.Locked;
        ApplyOverlayWindowStyle();
        SaveSettings();
        AppLogger.Info($"Overlay {(_settings.Locked ? "locked (click-through)" : "unlocked")}");
    }

    private void ToggleVisibility()
    {
        _settings.Hidden = IsVisible;
        if (_settings.Hidden) Hide(); else Show();
        SaveSettings();
        AppLogger.Info($"Overlay {(_settings.Hidden ? "hidden" : "shown")}");
    }

    private void RefreshOverlay()
    {
        _forceStatsRefresh = true;
        _watcher.Refresh();
    }

    private void DragWhenUnlocked(object sender, MouseButtonEventArgs e)
    {
        if (!_settings.Locked && e.LeftButton == MouseButtonState.Pressed && !IsButtonSource(e.OriginalSource)) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static bool IsButtonSource(object source)
    {
        for (var current = source as DependencyObject; current is Visual visual; current = VisualTreeHelper.GetParent(visual))
            if (current is Button) return true;
        return false;
    }

    private void SaveSettings()
    {
        if (_mockMode) return;
        if (!double.IsNaN(Left)) _settings.X = Left;
        if (!double.IsNaN(Top)) _settings.Y = Top;
        _settings.Opacity = Opacity;
        _settings.Hidden = !IsVisible;
        _settingsService.Save(_settings);
    }

    private void ShowMockData()
    {
        _players.Clear();
        var samples = new[]
        {
            new { Name = "Gualord", ProfileId = 1, Slot = 1, Color = 1, Team = 1, OneVsOne = 939, TeamRating = 1039, Wins = 70, Losses = 65, Civs = new[] { "PER", "VIK", "HUN", "MAY", "BER" } },
            new { Name = "Hera", ProfileId = 2, Slot = 2, Color = 2, Team = 1, OneVsOne = 2612, TeamRating = 1548, Wins = 124, Losses = 61, Civs = new[] { "MAY", "MON", "HUN", "VIK", "CHI" } },
            new { Name = "Liereyy", ProfileId = 3, Slot = 3, Color = 3, Team = 1, OneVsOne = 2580, TeamRating = 1602, Wins = 91, Losses = 58, Civs = new[] { "ETH", "MAL", "MON", "HIN", "FRA" } },
            new { Name = "Yo", ProfileId = 4, Slot = 4, Color = 4, Team = 1, OneVsOne = 1784, TeamRating = 1640, Wins = 88, Losses = 72, Civs = new[] { "BRI", "JAP", "SAR", "BYZ", "TEU" } },
            new { Name = "Viper", ProfileId = 5, Slot = 5, Color = 5, Team = 2, OneVsOne = 2475, TeamRating = 1778, Wins = 118, Losses = 54, Civs = new[] { "NOR", "POL", "BUR", "INC", "AZT" } },
            new { Name = "TaToH", ProfileId = 6, Slot = 6, Color = 6, Team = 2, OneVsOne = 2301, TeamRating = 1711, Wins = 109, Losses = 66, Civs = new[] { "SPA", "POR", "TUR", "KOR", "SLA" } },
            new { Name = "Vinchester", ProfileId = 7, Slot = 7, Color = 7, Team = 2, OneVsOne = 2148, TeamRating = 1682, Wins = 97, Losses = 69, Civs = new[] { "RUS", "MAG", "LIT", "BUL", "CUM" } },
            new { Name = "MbL", ProfileId = 8, Slot = 8, Color = 8, Team = 2, OneVsOne = 2056, TeamRating = 1594, Wins = 82, Losses = 73, Civs = new[] { "GOT", "CEL", "FRA", "HUN", "MAL" } }
        };
        int? previousTeam = null;
        foreach (var sample in samples)
        {
            var player = new OverlayPlayerViewModel
            {
                Name = sample.Name, ProfileId = sample.ProfileId, Slot = sample.Slot, Color = sample.Color, Team = sample.Team,
                ShowTeamSeparator = previousTeam is not null && previousTeam != sample.Team
            };
            player.Apply(new PlayerStatistics
            {
                ProfileId = sample.ProfileId, OneVsOneRating = sample.OneVsOne, TeamRating = sample.TeamRating,
                OneVsOneWins = sample.Wins, OneVsOneLosses = sample.Losses, RecentCivilizations = sample.Civs
            });
            _players.Add(player);
            previousTeam = sample.Team;
        }
        StatusText.Visibility = Visibility.Collapsed;
    }
}
