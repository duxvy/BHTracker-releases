using System.Text.Json;

namespace BHTracker.Hub;

/// <summary>An official Brawlhalla post from Steam: its id, headline, time, BBCode and the link to the full post.</summary>
public sealed record Post(string Id, string Title, DateTimeOffset Date, string Contents)
{
    public string Url => SteamNews.PostUrl(Id);
}

/// <summary>Brawlhalla's news from Steam's public news API (no key). Only Blue Mammoth's own announcements count:
/// the feed also carries news sites' articles, so the API is asked for that feed alone, and the answer checked too.</summary>
public static class SteamNews
{
    public const string AppId = "291550";
    public const string OfficialFeed = "steam_community_announcements";
    /// <summary>The most the API gives in one answer.</summary>
    public const int Page = 100;

    /// <param name="before">Only posts before this time (unix seconds): the next page back.</param>
    public static string ApiUrl(int count, long? before = null) =>
        $"https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid={AppId}&count={count}&maxlength=0&format=json&feeds={OfficialFeed}"
        + (before is { } b ? $"&enddate={b}" : "");

    public static string PostUrl(string id) => $"https://store.steampowered.com/news/app/{AppId}/view/{id}";

    public static async Task<string> FetchAsync(HttpClient http, int count, CancellationToken ct) =>
        await http.GetStringAsync(ApiUrl(count), ct);

    /// <summary>Every official post the API has, a page of 100 at a time back to the first (996 in October 2026, back
    /// to April 2014): about ten answers, a second apart.</summary>
    public static async Task<IReadOnlyList<Post>> FetchAllAsync(HttpClient http, CancellationToken ct, Func<TimeSpan, Task>? wait = null)
    {
        wait ??= d => Task.Delay(d, ct);
        var all = new List<Post>();
        long? before = null;
        for (int page = 0; page < 40; page++)
        {
            if (page > 0) await wait(TimeSpan.FromSeconds(1));
            var posts = Official(await http.GetStringAsync(ApiUrl(Page, before), ct), dedupe: false);
            if (posts.Count == 0) break;
            all.AddRange(posts);
            var oldest = posts.Min(p => p.Date).ToUnixTimeSeconds();
            if (before is { } b && oldest >= b) break;   // no further back
            before = oldest - 1;
            if (posts.Count < Page / 2) break;
        }
        return Dedupe(all);
    }

    /// <summary>The official posts in a GetNewsForApp answer, newest first, each once. Throws on an answer that isn't one.</summary>
    public static IReadOnlyList<Post> Official(string json) => Official(json, dedupe: true);

    static IReadOnlyList<Post> Official(string json, bool dedupe)
    {
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("appnews").GetProperty("newsitems");
        var posts = new List<Post>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.GetProperty("feedname").GetString() != OfficialFeed) continue;
            var id = item.GetProperty("gid").GetString();
            var title = item.GetProperty("title").GetString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || !id.All(char.IsAsciiDigit)) continue;
            posts.Add(new(id, Bbcode.Plain(title), DateTimeOffset.FromUnixTimeSeconds(item.GetProperty("date").GetInt64()),
                item.GetProperty("contents").GetString() ?? ""));
        }
        return dedupe ? Dedupe(posts) : posts;
    }

    /// <summary>Each post once, newest first: the same id, or the same headline the same day (older posts are often on
    /// Steam twice).</summary>
    public static IReadOnlyList<Post> Dedupe(IEnumerable<Post> posts) =>
        posts.DistinctBy(p => p.Id).OrderByDescending(p => p.Date).ThenBy(p => p.Id, StringComparer.Ordinal)
            .DistinctBy(p => (p.Title, DateOnly.FromDateTime(p.Date.UtcDateTime))).ToList();
}
