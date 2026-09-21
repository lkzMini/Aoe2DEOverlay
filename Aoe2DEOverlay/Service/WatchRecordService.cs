using System.IO;
using ReadAoe2Recrod;

namespace Aoe2DEOverlay;

public sealed class WatchRecordService : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan[] RetryDelays = { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8) };
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private string? _parsedReplayPath;

    public event Action<string>? StateChanged;
    public event Action<Match>? MatchDetected;
    public IReadOnlyList<string> SaveGameDirectories { get; private set; } = Array.Empty<string>();

    public void Start()
    {
        var basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Age of Empires 2 DE");
        if (!Directory.Exists(basePath))
        {
            AppLogger.Warning($"AoE2 save root not found: {basePath}");
            StateChanged?.Invoke("AoE2 · waiting for match");
            return;
        }

        SaveGameDirectories = Directory.EnumerateDirectories(basePath)
            .Where(path => !string.Equals(Path.GetFileName(path), "0", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.Combine(path, "savegame"))
            .Where(Directory.Exists)
            .ToArray();

        foreach (var saveDirectory in SaveGameDirectories)
        {
            var watcher = new FileSystemWatcher(saveDirectory, "*.aoe2record")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            watcher.Created += OnReplayChanged;
            watcher.Changed += OnReplayChanged;
            watcher.Renamed += OnReplayChanged;
            watcher.Error += (_, args) => AppLogger.Error("Replay watcher error", args.GetException());
            _watchers.Add(watcher);
        }

        AppLogger.Info($"Watching {SaveGameDirectories.Count} savegame director{(SaveGameDirectories.Count == 1 ? "y" : "ies")}: {string.Join("; ", SaveGameDirectories)}");
        StateChanged?.Invoke("AoE2 · waiting for match");
        ScheduleLatest(TimeSpan.FromMilliseconds(300));
    }

    public void Refresh()
    {
        lock (_gate) _parsedReplayPath = null;
        AppLogger.Info("Manual refresh requested");
        ScheduleLatest(TimeSpan.Zero);
    }

    private void OnReplayChanged(object sender, FileSystemEventArgs args)
    {
        AppLogger.Info($"Replay event {args.ChangeType}: {Path.GetFileName(args.FullPath)}");
        ScheduleLatest(DebounceDelay);
    }

    private void ScheduleLatest(TimeSpan delay)
    {
        CancellationToken token;
        lock (_gate)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = new CancellationTokenSource();
            token = _pending.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token);
                var latest = FindLatestReplay();
                if (latest is null) return;
                lock (_gate)
                {
                    if (string.Equals(_parsedReplayPath, latest, StringComparison.OrdinalIgnoreCase)) return;
                }
                await ReadWithRetriesAsync(latest, token);
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private string? FindLatestReplay() => SaveGameDirectories
        .SelectMany(path => Directory.EnumerateFiles(path, "*.aoe2record", SearchOption.TopDirectoryOnly))
        .Select(path => new FileInfo(path))
        .OrderByDescending(file => file.LastWriteTimeUtc)
        .FirstOrDefault()?.FullName;

    private async Task ReadWithRetriesAsync(string replayPath, CancellationToken cancellationToken)
    {
        StateChanged?.Invoke("Reading match…");
        AppLogger.Info($"Replay detected: {replayPath}");
        Exception? lastError = null;

        for (var attempt = 0; attempt < RetryDelays.Length; attempt++)
        {
            try
            {
                await Task.Delay(RetryDelays[attempt], cancellationToken);
                var record = Aoe2Record.ReadFile(replayPath);
                var players = record.Players.Where(player => player.TypeId != 6).Select(player => new Player
                {
                    Id = unchecked((int)player.ProfileId), Slot = player.Slot, Color = player.Color,
                    Team = player.Team, Name = player.Name, Civ = player.Civ
                }).ToArray();
                if (players.Length == 0) throw new InvalidDataException("The replay header contained no active players.");

                var info = new FileInfo(replayPath);
                var match = new Match
                {
                    ReplayPath = replayPath,
                    Identity = $"{replayPath}|{info.CreationTimeUtc.Ticks}",
                    Started = record.Started,
                    IsMultiplayer = record.IsMultiplayer,
                    Players = players
                };
                lock (_gate) _parsedReplayPath = replayPath;
                AppLogger.Info($"Replay parsed: {players.Length} players; {string.Join(", ", players.Select(player => $"{player.Name} ({player.Id}) team {player.Team}"))}");
                MatchDetected?.Invoke(match);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastError = exception;
                if (attempt == 0) AppLogger.Warning($"Replay is not ready; bounded retry started: {exception.Message}");
            }
        }

        AppLogger.Error($"Replay parse failed after {RetryDelays.Length} attempts: {replayPath}", lastError);
        StateChanged?.Invoke("AoE2 · waiting for match");
    }

    public void Dispose()
    {
        lock (_gate) { _pending?.Cancel(); _pending?.Dispose(); }
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }
}


