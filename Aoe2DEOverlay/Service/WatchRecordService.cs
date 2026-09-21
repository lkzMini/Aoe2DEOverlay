using System.IO;
using ReadAoe2Recrod;

namespace Aoe2DEOverlay;

public sealed class WatchRecordService : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan DiscoveryInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] RetryDelays = { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8) };
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly string _basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Age of Empires 2 DE");
    private CancellationTokenSource? _pending;
    private System.Threading.Timer? _discoveryTimer;
    private string? _parsedReplayPath;
    private bool _rootMissingLogged;
    private bool _disposed;

    public event Action<string>? StateChanged;
    public event Action<Match>? MatchDetected;
    public IReadOnlyList<string> SaveGameDirectories { get; private set; } = Array.Empty<string>();

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed) return;
        }
        RediscoverSaveDirectories();
        lock (_gate)
        {
            if (_disposed) return;
            _discoveryTimer = new System.Threading.Timer(_ => RediscoverSaveDirectories(), null, DiscoveryInterval, DiscoveryInterval);
        }
        StateChanged?.Invoke("AoE2 · waiting for match");
        ScheduleLatest(TimeSpan.FromMilliseconds(300));
    }

    public void Refresh()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _parsedReplayPath = null;
        }
        AppLogger.Info("Manual refresh requested");
        RediscoverSaveDirectories();
        ScheduleLatest(TimeSpan.Zero);
    }

    private void RediscoverSaveDirectories()
    {
        if (_disposed) return;
        try
        {
            if (!Directory.Exists(_basePath))
            {
                var shouldLog = false;
                lock (_gate)
                {
                    if (_disposed) return;
                    shouldLog = !_rootMissingLogged;
                    _rootMissingLogged = true;
                }
                if (shouldLog) AppLogger.Warning($"AoE2 save root not found; discovery will retry: {_basePath}");
                UpdateDirectorySnapshot(Array.Empty<string>());
                return;
            }

            var discovered = Directory.EnumerateDirectories(_basePath)
                .Where(path => !string.Equals(Path.GetFileName(path), "0", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.Combine(path, "savegame"))
                .Where(Directory.Exists)
                .ToArray();

            var addedAny = false;
            lock (_gate)
            {
                if (_disposed) return;
                _rootMissingLogged = false;
                foreach (var missing in _watchers.Keys.Except(discovered, StringComparer.OrdinalIgnoreCase).ToArray())
                {
                    _watchers[missing].Dispose();
                    _watchers.Remove(missing);
                }

                foreach (var saveDirectory in discovered)
                {
                    if (_watchers.ContainsKey(saveDirectory)) continue;
                    _watchers[saveDirectory] = CreateWatcher(saveDirectory);
                    addedAny = true;
                }
                SaveGameDirectories = discovered;
            }

            if (addedAny)
            {
                AppLogger.Info($"Watching {discovered.Length} savegame director{(discovered.Length == 1 ? "y" : "ies")}: {string.Join("; ", discovered)}");
                ScheduleLatest(TimeSpan.FromMilliseconds(300));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.Error("Savegame directory discovery failed; it will retry", exception);
        }
    }

    private void UpdateDirectorySnapshot(IReadOnlyList<string> directories)
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var watcher in _watchers.Values) watcher.Dispose();
            _watchers.Clear();
            SaveGameDirectories = directories;
        }
    }

    private FileSystemWatcher CreateWatcher(string saveDirectory)
    {
        var watcher = new FileSystemWatcher(saveDirectory, "*.aoe2record")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false
        };
        watcher.Created += OnReplayChanged;
        watcher.Changed += OnReplayChanged;
        watcher.Renamed += OnReplayChanged;
        watcher.Error += OnWatcherError;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        if (sender is FileSystemWatcher failedWatcher)
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (_watchers.Remove(failedWatcher.Path)) failedWatcher.Dispose();
            }
        }
        AppLogger.Error("Replay watcher error; rebuilding watchers", args.GetException());
        RediscoverSaveDirectories();
        ScheduleLatest(TimeSpan.FromMilliseconds(300));
    }

    private void OnReplayChanged(object sender, FileSystemEventArgs args)
    {
        lock (_gate) if (_disposed) return;
        AppLogger.Info($"Replay event {args.ChangeType}: {Path.GetFileName(args.FullPath)}");
        ScheduleLatest(DebounceDelay);
    }

    private void ScheduleLatest(TimeSpan delay)
    {
        if (_disposed) return;
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed) return;
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

    private string? FindLatestReplay()
    {
        string[] directories;
        lock (_gate)
        {
            if (_disposed) return null;
            directories = SaveGameDirectories.ToArray();
        }
        var files = new List<FileInfo>();
        foreach (var directory in directories)
        {
            try
            {
                files.AddRange(Directory.EnumerateFiles(directory, "*.aoe2record", SearchOption.TopDirectoryOnly).Select(path => new FileInfo(path)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                AppLogger.Warning($"Could not scan savegame directory {directory}: {exception.Message}");
            }
        }
        return files.OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()?.FullName;
    }

    private async Task ReadWithRetriesAsync(string replayPath, CancellationToken cancellationToken)
    {
        lock (_gate) if (_disposed) return;
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
                lock (_gate)
                {
                    if (_disposed) return;
                    _parsedReplayPath = replayPath;
                }
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

        lock (_gate) if (_disposed) return;
        AppLogger.Error($"Replay parse failed after {RetryDelays.Length} attempts: {replayPath}", lastError);
        StateChanged?.Invoke("AoE2 · waiting for match");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _discoveryTimer?.Dispose();
            _pending?.Cancel();
            _pending?.Dispose();
            foreach (var watcher in _watchers.Values) watcher.Dispose();
            _watchers.Clear();
            SaveGameDirectories = Array.Empty<string>();
        }
    }
}
