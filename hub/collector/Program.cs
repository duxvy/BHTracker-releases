using System.Globalization;
using System.Net;
using System.Text.Json;
using BHTracker.Hub;

// The Hub's collector. One run: Brawlhalla's official Steam posts, Liquipedia's tournament list (kept 12 hours), the
// approved dates.json -> hub.json and date-suggestions.json in --out, each written only when its content changed.
//
//   --out <dir>              where the two files go (the previous hub.json there is kept for a source that fails)
//   --dates <file>           dates.json (approved dates)
//   --cache <dir>            Liquipedia's answers between runs
//   --steam-file <file>      Steam's answer from a file instead of the API (tests, offline)
//   --liquipedia-file <file> Liquipedia's HTML from a file instead of the API
//   --no-liquipedia          leave results as they were
//   --now <time>             the run's time (tests)
//   --check-dates <file>     only check a dates.json and list its entries

var options = Options(args);
if (options.TryGetValue("check-dates", out var check)) return CheckDates(check);
if (!options.TryGetValue("out", out var outDir)) { Console.Error.WriteLine("--out <dir> is needed"); return 2; }

var now = options.TryGetValue("now", out var nowText) ? DateTimeOffset.Parse(nowText, CultureInfo.InvariantCulture) : DateTimeOffset.UtcNow;
var hubPath = Path.Combine(outDir, "hub.json");
var previous = File.Exists(hubPath) ? HubDocument.FromJson(File.ReadAllText(hubPath)) : null;

IReadOnlyList<HubDate> dates = [];
if (options.TryGetValue("dates", out var datesPath))
{
    var (read, problems) = DatesFile.Read(File.ReadAllText(datesPath));
    foreach (var p in problems) Console.WriteLine($"::warning::dates.json: {p} (left out)");
    dates = read;
}

using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
IReadOnlyList<Post>? posts = null;
try
{
    var json = options.TryGetValue("steam-file", out var steamFile) ? File.ReadAllText(steamFile) : await SteamAsync(cts.Token);
    posts = SteamNews.Official(json);
    Console.WriteLine($"Steam: {posts.Count} official posts");
}
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
{
    Console.WriteLine($"::warning::Steam news failed ({ex.GetType().Name}: {ex.Message}); keeping the last news");
}

IReadOnlyList<string>? pages = null;
if (options.TryGetValue("liquipedia-file", out var lpFile)) pages = [LiquipediaHtml(File.ReadAllText(lpFile))];
else if (!options.ContainsKey("no-liquipedia"))
{
    try { pages = await LiquipediaAsync(options.GetValueOrDefault("cache"), now, cts.Token); }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
    {
        Console.WriteLine($"::warning::Liquipedia failed ({ex.GetType().Name}: {ex.Message}); keeping the last results");
    }
}

if (posts is null && previous is null) { Console.Error.WriteLine("No news and no earlier hub.json: nothing to write"); return 1; }

var inputs = new HubInputs(posts, pages, dates, previous, now);
var hub = Collector.Build(inputs, Names.Bundled);
bool wroteHub = Collector.WriteHub(hubPath, hub, now);
bool wroteSuggestions = Collector.WriteSuggestions(Path.Combine(outDir, "date-suggestions.json"), Collector.Suggest(inputs));
Console.WriteLine($"hub.json {(wroteHub ? "updated" : "unchanged")}: {hub.News.Count} news, {hub.Patches.Count} patches, " +
    $"{hub.Esports.Upcoming.Count} upcoming, {hub.Esports.Results.Count} results, {hub.Countdowns.Count} countdowns");
Console.WriteLine($"date-suggestions.json {(wroteSuggestions ? "updated" : "unchanged")}");
return 0;

static async Task<string> SteamAsync(CancellationToken ct)
{
    using var http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.GZip }) { Timeout = TimeSpan.FromSeconds(30) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd(Liquipedia.UserAgent);
    for (int attempt = 1; ; attempt++)
    {
        try { return await SteamNews.FetchAsync(http, 50, ct); }
        catch (HttpRequestException) when (attempt < 3) { await Task.Delay(TimeSpan.FromSeconds(10 * attempt), ct); }
    }
}

// this year's list (and last year's in January and February), each kept for 12 hours in the cache folder
static async Task<IReadOnlyList<string>> LiquipediaAsync(string? cacheDir, DateTimeOffset now, CancellationToken ct)
{
    var years = now.Month <= 2 ? new[] { now.Year, now.Year - 1 } : [now.Year];
    using var wiki = new Liquipedia();
    var pages = new List<string>();
    foreach (var year in years)
    {
        var cached = cacheDir is null ? null : Path.Combine(cacheDir, $"liquipedia-{year}.json");
        if (cached is not null && File.Exists(cached))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(cached));
            if (now - doc.RootElement.GetProperty("fetched").GetDateTimeOffset() < TimeSpan.FromHours(12))
            {
                pages.Add(doc.RootElement.GetProperty("html").GetString() ?? "");
                Console.WriteLine($"Liquipedia {year}: kept from {doc.RootElement.GetProperty("fetched").GetDateTimeOffset():u}");
                continue;
            }
        }
        var html = await wiki.TournamentsAsync(year, ct);
        pages.Add(html);
        Console.WriteLine($"Liquipedia {year}: fetched");
        if (cached is not null)
        {
            Directory.CreateDirectory(cacheDir!);
            File.WriteAllText(cached, JsonSerializer.Serialize(new { fetched = now, html }));
        }
    }
    return pages;
}

// a saved answer: the API's JSON, or the HTML itself
static string LiquipediaHtml(string text)
{
    if (!text.TrimStart().StartsWith('{')) return text;
    using var doc = JsonDocument.Parse(text);
    return doc.RootElement.GetProperty("parse").GetProperty("text").GetString() ?? "";
}

static int CheckDates(string path)
{
    var (dates, problems) = DatesFile.Read(File.ReadAllText(path));
    foreach (var d in dates) Console.WriteLine($"ok   {d.Kind,-11} {d.Name}: {d.Start ?? "-"} to {d.End ?? "-"}{(d.Url is null ? "" : $" ({d.Url})")}");
    foreach (var p in problems) Console.WriteLine($"BAD  {p}");
    return problems.Count == 0 ? 0 : 1;
}

static Dictionary<string, string> Options(string[] args)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length; i++)
        if (args[i].StartsWith("--", StringComparison.Ordinal))
            map[args[i][2..]] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "";
    return map;
}
