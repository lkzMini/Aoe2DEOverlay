using System.Runtime.InteropServices;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Aoe2DEOverlay;

public partial class MainWindow : Window
{
    private const int ToggleLockHotkey = 1;
    private const int ToggleVisibilityHotkey = 2;
    private const int RefreshHotkey = 3;
    private readonly ObservableCollection<OverlayPlayerViewModel> _players = new();
    private readonly WatchRecordService _watcher = new();
    private readonly PlayerStatsService _stats = new();
    private readonly SettingsService _settingsService = new();
    private readonly OverlaySettings _settings;
    private CancellationTokenSource? _statsCancellation;
    private HwndSource? _windowSource;
    private nint _windowHandle;
    private bool _forceStatsRefresh;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        ApplySavedSettings();
        PlayersList.ItemsSource = _players;
        _watcher.StateChanged += SetStatus;
        _watcher.MatchDetected += MatchDetected;
        Loaded += (_, _) => Start();
        MouseLeftButtonDown += DragWhenUnlocked;
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
        if (_settings.Hidden) Hide();
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
        UnlockedIndicator.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool IsVisibleCoordinate(double value, double origin, double length) => !double.IsNaN(value) && value >= origin - 100 && value <= origin + length - 40;

    private void ApplyOverlayWindowStyle()
    {
        if (_windowHandle == 0) return;
        var style = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle);
        style |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
        style = _settings.Locked ? style | NativeMethods.WsExTransparent : style & ~NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle, style);
        UnlockedIndicator.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RegisterGlobalHotkeys()
    {
        RegisterHotkey(ToggleLockHotkey, 0x4F, "Ctrl+Shift+O");
        RegisterHotkey(ToggleVisibilityHotkey, 0x48, "Ctrl+Shift+H");
        RegisterHotkey(RefreshHotkey, 0x52, "Ctrl+Shift+R");
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
        if (!_settings.Locked && e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void SaveSettings()
    {
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
}
