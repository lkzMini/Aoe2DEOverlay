using ReadAoe2Recrod;

var path = args.FirstOrDefault();
if (string.IsNullOrWhiteSpace(path))
{
    var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Age of Empires 2 DE");
    path = Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "*.aoe2record", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault()
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
    return record.Players.Length > 0 ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Replay parse failed: {exception.Message}");
    return 1;
}
