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

    public PlayerStatsService() : this(new HttpClientHandler()) { }

    public PlayerStatsService(HttpMessageHandler handler)
    {
        _httpClient = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(10) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AoE2MinimalOverlay/2.0");
    }

    public async Task<IReadOnlyDictionary<int, PlayerStatistics>> GetAsync(
        IEnumerable<Player> players,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var profileIds = players.Where(player => !player.IsAi && player.Id > 0).Select(player => player.Id).Distinct().ToArray();
        var result = new Dictionary<int, PlayerStatistics>();
        var missing = new List<int>();
        var now = DateTimeOffset.UtcNow;

        foreach (var profileId in profileIds)
        {
            if (!forceRefresh && _cache.TryGetValue(profileId, out var cached) && cached.IsFresh(now))
                result[profileId] = cached.Statistics;
            else
                missing.Add(profileId);
        }
        if (missing.Count == 0) return result;

        Dictionary<int, PlayerStatistics> ratings;
        var ratingsSucceeded = false;
        try
        {
            ratings = await FetchRatingsAsync(missing, cancellationToken);
            ratingsSucceeded = true;
        }
        catch (Exception exception) when (IsRecoverableStatsFailure(exception, cancellationToken))
        {
            ratings = new Dictionary<int, PlayerStatistics>();
            AppLogger.Error($"Ratings refresh failed for profile IDs {string.Join(",", missing)}", exception);
        }

        var historyTasks = missing.ToDictionary(profileId => profileId, profileId => FetchRecentCivilizationsAsync(profileId, cancellationToken));
        await Task.WhenAll(historyTasks.Values);
        now = DateTimeOffset.UtcNow;

        foreach (var profileId in missing)
        {
            _cache.TryGetValue(profileId, out var oldEntry);
            var usableStale = oldEntry?.UsableAt(now) ?? new PlayerStatistics { ProfileId = profileId };
            var freshRatings = ratings.GetValueOrDefault(profileId) ?? new PlayerStatistics { ProfileId = profileId };
            var history = await historyTasks[profileId];
            var merged = MergeComponents(freshRatings, usableStale, history);

            var usedStaleRatings = ratingsSucceeded && UsesStaleRatings(freshRatings, usableStale);
            var usedStaleHistory = history.Succeeded && history.Civilizations.Count == 0 && usableStale.RecentCivilizations.Count > 0;
            var ratingsFetchedAt = ratingsSucceeded && !usedStaleRatings ? now : oldEntry?.RatingsFetchedAt ?? DateTimeOffset.MinValue;
            var historyFetchedAt = history.Succeeded && !usedStaleHistory ? now : oldEntry?.HistoryFetchedAt ?? DateTimeOffset.MinValue;

            _cache[profileId] = new CacheEntry(merged, ratingsFetchedAt, historyFetchedAt);
            result[profileId] = merged;
        }

        return result;
    }

    private static PlayerStatistics MergeComponents(PlayerStatistics fresh, PlayerStatistics stale, HistoryResult history) => new()
    {
        ProfileId = fresh.ProfileId,
        OneVsOneRating = fresh.OneVsOneRating ?? stale.OneVsOneRating,
        OneVsOneWins = fresh.OneVsOneWins ?? stale.OneVsOneWins,
        OneVsOneLosses = fresh.OneVsOneLosses ?? stale.OneVsOneLosses,
        TeamRating = fresh.TeamRating ?? stale.TeamRating,
        TeamWins = fresh.TeamWins ?? stale.TeamWins,
        TeamLosses = fresh.TeamLosses ?? stale.TeamLosses,
        RecentCivilizations = history.Succeeded && history.Civilizations.Count > 0
            ? history.Civilizations
            : stale.RecentCivilizations
    };

    private static bool UsesStaleRatings(PlayerStatistics fresh, PlayerStatistics stale) =>
        fresh.OneVsOneRating is null && stale.OneVsOneRating is not null ||
        fresh.OneVsOneWins is null && stale.OneVsOneWins is not null ||
        fresh.OneVsOneLosses is null && stale.OneVsOneLosses is not null ||
        fresh.TeamRating is null && stale.TeamRating is not null ||
        fresh.TeamWins is null && stale.TeamWins is not null ||
        fresh.TeamLosses is null && stale.TeamLosses is not null;

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

    private async Task<HistoryResult> FetchRecentCivilizationsAsync(int profileId, CancellationToken cancellationToken)
    {
        await _historyConcurrency.WaitAsync(cancellationToken);
        try
        {
            var oneVsOne = await FetchMatchListAsync(profileId, 3, cancellationToken);
            var civilizations = oneVsOne.Count > 0 ? oneVsOne : await FetchMatchListAsync(profileId, 4, cancellationToken);
            return new HistoryResult(civilizations, true);
        }
        catch (Exception exception) when (IsRecoverableStatsFailure(exception, cancellationToken))
        {
            AppLogger.Warning($"Recent civilization request failed for profile {profileId}: {exception.Message}");
            return new HistoryResult(Array.Empty<string>(), false);
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
            .Where(name => !string.IsNullOrWhiteSpace(name)).Take(5)
            .Select(name => AbbreviateCivilization(name!)).ToArray()!;
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

    private static bool IsRecoverableStatsFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException ||
        exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;
    private static bool IsTransient(HttpStatusCode statusCode) => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
    private static int GetOptionalInt(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.GetInt32() : 0;
    private static string AbbreviateCivilization(string name) => new(name.Where(char.IsLetter).Take(3).Select(char.ToUpperInvariant).ToArray());

    public void Dispose()
    {
        _httpClient.Dispose();
        _historyConcurrency.Dispose();
    }

    private sealed record HistoryResult(IReadOnlyList<string> Civilizations, bool Succeeded);
    private sealed record CacheEntry(PlayerStatistics Statistics, DateTimeOffset RatingsFetchedAt, DateTimeOffset HistoryFetchedAt)
    {
        public bool IsFresh(DateTimeOffset now) => now - RatingsFetchedAt <= FreshFor && now - HistoryFetchedAt <= FreshFor;
        public PlayerStatistics UsableAt(DateTimeOffset now) => new()
        {
            ProfileId = Statistics.ProfileId,
            OneVsOneRating = now - RatingsFetchedAt <= StaleFor ? Statistics.OneVsOneRating : null,
            OneVsOneWins = now - RatingsFetchedAt <= StaleFor ? Statistics.OneVsOneWins : null,
            OneVsOneLosses = now - RatingsFetchedAt <= StaleFor ? Statistics.OneVsOneLosses : null,
            TeamRating = now - RatingsFetchedAt <= StaleFor ? Statistics.TeamRating : null,
            TeamWins = now - RatingsFetchedAt <= StaleFor ? Statistics.TeamWins : null,
            TeamLosses = now - RatingsFetchedAt <= StaleFor ? Statistics.TeamLosses : null,
            RecentCivilizations = now - HistoryFetchedAt <= StaleFor ? Statistics.RecentCivilizations : Array.Empty<string>()
        };
    }

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


