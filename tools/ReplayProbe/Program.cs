using System.Net;
using System.Text;
using Aoe2DEOverlay;
using ReadAoe2Recrod;

if (args.Contains("--cache-probe", StringComparer.OrdinalIgnoreCase))
    return await RunCacheProbeAsync();

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

static async Task<int> RunCacheProbeAsync()
{
    using var service = new PlayerStatsService(new PartialFailureHandler());
    var player = new Player { Id = 42, Name = "Cache Probe" };
    var first = (await service.GetAsync(new[] { player }))[42];
    var second = (await service.GetAsync(new[] { player }, forceRefresh: true))[42];
    var third = (await service.GetAsync(new[] { player }, forceRefresh: true))[42];
    var passed = first.OneVsOneRating == 1500 && second.OneVsOneRating == 1500 && third.OneVsOneRating == 1500 &&
                 first.RecentCivilizations.SequenceEqual(new[] { "MAY" }) &&
                 second.RecentCivilizations.SequenceEqual(new[] { "MAY" }) &&
                 third.RecentCivilizations.SequenceEqual(new[] { "MAY" });
    Console.WriteLine($"Partial-response stale cache probe: {(passed ? "PASS" : "FAIL")}");
    return passed ? 0 : 1;
}

sealed class PartialFailureHandler : HttpMessageHandler
{
    private int _ratingRequests;
    private int _historyRequests;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get)
        {
            _ratingRequests++;
            if (_ratingRequests == 1)
                return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                    "{\"statGroups\":[{\"id\":7,\"members\":[{\"profile_id\":42}]}],\"leaderboardStats\":[{\"statgroup_id\":7,\"leaderboard_id\":3,\"wins\":6,\"losses\":4,\"drops\":0,\"rating\":1500}]}"));
            if (_ratingRequests == 2)
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{\"statGroups\":[],\"leaderboardStats\":[]}"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ThrowingContent() });
        }

        _historyRequests++;
        return Task.FromResult(_historyRequests == 1
            ? JsonResponse(HttpStatusCode.OK, "{\"matchList\":[{\"civilization\":\"Mayans\"}]}")
            : JsonResponse(HttpStatusCode.InternalServerError, "{}"));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}

sealed class ThrowingContent : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        Task.FromException(new IOException("Simulated response-body interruption"));

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
