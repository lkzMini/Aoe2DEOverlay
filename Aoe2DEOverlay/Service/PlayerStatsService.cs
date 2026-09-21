using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Aoe2DEOverlay;

public sealed class PlayerStatsService : IDisposable
{
    private static readonly Uri PersonalStatsBase = new("https://aoe-api.worldsedgelink.com/community/leaderboard/getPersonalStat");
    private static readonly Uri MatchListEndpoint = new("https://api.ageofempires.com/api/GameStats/AgeII/GetMatchList");
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StaleFor = TimeSpan.FromHours(24);
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<int, CacheEntry> _cache = new();
    private readonly SemaphoreSlim _historyConcurrency = new(4);

    public PlayerStatsService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AoE2MinimalOverlay/2.0");
    }

    public async Task<IReadOnlyDictionary<int, PlayerStatistics>> GetAsync(
        IEnumerable<Player> players,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var profileIds = players.Where(player => !player.IsAi && player.Id > 0)
            .Select(player => player.Id).Distinct().ToArray();
        var result = new Dictionary<int, PlayerStatistics>();
        var missing = new List<int>();

        foreach (var profileId in profileIds)
        {
            if (!forceRefresh && _cache.TryGetValue(profileId, out var cached) && DateTimeOffset.UtcNow - cached.FetchedAt <= FreshFor)
                result[profileId] = cached.Statistics;
            else
                missing.Add(profileId);
        }

        if (missing.Count == 0) return result;

        try
        {
            var ratings = await FetchRatingsAsync(missing, cancellationToken);
            var tasks = missing.Select(async profileId =>
            {
                var stats = ratings.GetValueOrDefault(profileId) ?? new PlayerStatistics { ProfileId = profileId };
                var civilizations = await FetchRecentCivilizationsAsync(profileId, cancellationToken);
                return stats with { RecentCivilizations = civilizations };
            });

            foreach (var stats in await Task.WhenAll(tasks))
            {
                result[stats.ProfileId] = stats;
                _cache[stats.ProfileId] = new CacheEntry(stats, DateTimeOffset.UtcNow);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            AppLogger.Error($"Stats refresh failed for profile IDs {string.Join(",", missing)}", exception);
            foreach (var profileId in missing)
            {
                if (_cache.TryGetValue(profileId, out var stale) && DateTimeOffset.UtcNow - stale.FetchedAt <= StaleFor)
                    result[profileId] = stale.Statistics;
                else
                    result[profileId] = new PlayerStatistics { ProfileId = profileId };
            }
        }

        return result;
    }

    private async Task<Dictionary<int, PlayerStatistics>> FetchRatingsAsync(IReadOnlyCollection<int> profileIds, CancellationToken cancellationToken)
    {
        var idsJson = JsonSerializer.Serialize(profileIds);
        var uri = new Uri($"{PersonalStatsBase}?title=age2&profile_ids={Uri.EscapeDataString(idsJson)}");
        AppLogger.Info($"Stats API ratings request: {string.Join(",", profileIds)}");
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, uri), cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var root = document.RootElement;
        var groupToProfile = new Dictionary<long, int>();

        if (root.TryGetProperty("statGroups", out var groups))
        {
            foreach (var group in groups.EnumerateArray())
            {
                var groupId = group.GetProperty("id").GetInt64();
                foreach (var member in group.GetProperty("members").EnumerateArray())
                    groupToProfile[groupId] = member.GetProperty("profile_id").GetInt32();
            }
        }

        var builders = profileIds.ToDictionary(id => id, id => new StatisticsBuilder(id));
        if (root.TryGetProperty("leaderboardStats", out var leaderboardStats))
        {
            foreach (var stat in leaderboardStats.EnumerateArray())
            {
                var groupId = stat.GetProperty("statgroup_id").GetInt64();
                if (!groupToProfile.TryGetValue(groupId, out var profileId) || !builders.TryGetValue(profileId, out var builder)) continue;
                var leaderboardId = stat.GetProperty("leaderboard_id").GetInt32();
                var wins = stat.GetProperty("wins").GetInt32();
                var losses = stat.GetProperty("losses").GetInt32() + GetOptionalInt(stat, "drops");
                var rating = stat.GetProperty("rating").GetInt32();
                if (leaderboardId == 3) builder.SetOneVsOne(rating, wins, losses);
                if (leaderboardId == 4) builder.SetTeam(rating, wins, losses);
            }
        }

        return builders.ToDictionary(pair => pair.Key, pair => pair.Value.Build());
    }

    private async Task<IReadOnlyList<string>> FetchRecentCivilizationsAsync(int profileId, CancellationToken cancellationToken)
    {
        await _historyConcurrency.WaitAsync(cancellationToken);
        try
        {
            var oneVsOne = await FetchMatchListAsync(profileId, 3, cancellationToken);
            return oneVsOne.Count > 0 ? oneVsOne : await FetchMatchListAsync(profileId, 4, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            AppLogger.Warning($"Recent civilization request failed for profile {profileId}: {exception.Message}");
            return Array.Empty<string>();
        }
        finally
        {
            _historyConcurrency.Release();
        }
    }

    private async Task<IReadOnlyList<string>> FetchMatchListAsync(int profileId, int matchType, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, MatchListEndpoint);
            request.Content = JsonContent.Create(new { profileId, matchType = matchType.ToString() });
            return request;
        }, cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("matchList", out var matches)) return Array.Empty<string>();

        return matches.EnumerateArray()
            .Select(match => match.TryGetProperty("civilization", out var civilization) ? civilization.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(5)
            .Select(name => AbbreviateCivilization(name!))
            .ToArray()!;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = requestFactory();
            try
            {
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.IsSuccessStatusCode) return response;
                if (attempt >= 2 || !IsTransient(response.StatusCode))
                {
                    response.EnsureSuccessStatusCode();
                    return response;
                }
                response.Dispose();
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 2) { }
            catch (HttpRequestException) when (attempt < 2) { }
            await Task.Delay(TimeSpan.FromMilliseconds(350 * (attempt + 1)), cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
    private static int GetOptionalInt(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.GetInt32() : 0;
    private static string AbbreviateCivilization(string name) => new(name.Where(char.IsLetter).Take(3).Select(char.ToUpperInvariant).ToArray());

    public void Dispose()
    {
        _httpClient.Dispose();
        _historyConcurrency.Dispose();
    }

    private sealed record CacheEntry(PlayerStatistics Statistics, DateTimeOffset FetchedAt);

    private sealed class StatisticsBuilder(int profileId)
    {
        private int? _oneVsOneRating, _oneVsOneWins, _oneVsOneLosses, _teamRating, _teamWins, _teamLosses;
        public void SetOneVsOne(int rating, int wins, int losses) => (_oneVsOneRating, _oneVsOneWins, _oneVsOneLosses) = (rating, wins, losses);
        public void SetTeam(int rating, int wins, int losses) => (_teamRating, _teamWins, _teamLosses) = (rating, wins, losses);
        public PlayerStatistics Build() => new()
        {
            ProfileId = profileId, OneVsOneRating = _oneVsOneRating, OneVsOneWins = _oneVsOneWins,
            OneVsOneLosses = _oneVsOneLosses, TeamRating = _teamRating, TeamWins = _teamWins, TeamLosses = _teamLosses
        };
    }
}

