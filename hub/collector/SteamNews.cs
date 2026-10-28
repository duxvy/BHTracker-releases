using System.Text.Json;

namespace BHTracker.Hub;

/// <summary>An official Brawlhalla post from Steam: its id, headline, time, BBCode and the link to the full post.</summary>
public sealed record Post(string Id, string Title, DateTimeOffset Date, string Contents)
{
    public string Url => SteamNews.PostUrl(Id);
}

/// <summary>Brawlhalla's news from Steam's public news API (no key). Only Blue Mammoth's own announcements count:
/// the feed also carries news sites' articles.</summary>
public static class SteamNews
{
    public const string AppId = "291550";
    public const string OfficialFeed = "steam_community_announcements";

    public static string ApiUrl(int count) =>
        $"https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid={AppId}&count={count}&maxlength=0&format=json";

    public static string PostUrl(string id) => $"https://store.steampowered.com/news/app/{AppId}/view/{id}";

    public static async Task<string> FetchAsync(HttpClient http, int count, CancellationToken ct) =>
        await http.GetStringAsync(ApiUrl(count), ct);

    /// <summary>The official posts in a GetNewsForApp answer, newest first. Throws on an answer that isn't one.</summary>
    public static IReadOnlyList<Post> Official(string json)
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
        return posts.OrderByDescending(p => p.Date).ToList();
    }
}
