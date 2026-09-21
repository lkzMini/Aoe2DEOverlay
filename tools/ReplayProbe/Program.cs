using Aoe2DEOverlay;
using ReadAoe2Recrod;

var includeStats = args.Contains("--stats", StringComparer.OrdinalIgnoreCase);
var path = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));
if (string.IsNullOrWhiteSpace(path))
{
    var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Age of Empires 2 DE");
    path = Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "*.aoe2record", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
        : null;
}

if (path is null || !File.Exists(path))
{
    Console.Error.WriteLine("No replay found. Pass an .aoe2record path as the first argument.");
    return 2;
}

try
{
    var record = Aoe2Record.ReadFile(path);
    Console.WriteLine($"Replay: {path}");
    Console.WriteLine($"Started: {record.Started}; multiplayer: {record.IsMultiplayer}; players: {record.Players.Length}");
    foreach (var player in record.Players)
        Console.WriteLine($"slot={player.Slot} profile={player.ProfileId} team={player.Team} civ={player.Civ} name={player.Name}");

    if (includeStats)
    {
        var players = record.Players.Where(player => !player.IsAi).Select(player => new Player { Id = (int)player.ProfileId, Name = player.Name }).ToArray();
        using var service = new PlayerStatsService();
        var stats = await service.GetAsync(players);
        foreach (var player in players)
        {
            var stat = stats[player.Id];
            Console.WriteLine($"stats profile={player.Id} 1v1={stat.OneVsOneRating} TG={stat.TeamRating} WR={stat.WinRate}% W/L={stat.DisplayWins}/{stat.DisplayLosses} civs={string.Join(',', stat.RecentCivilizations)}");
        }
    }

    return record.Players.Length > 0 ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Validation failed: {exception.Message}");
    return 1;
}
