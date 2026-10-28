using System.Net;
using System.Text.Json;

namespace BHTracker.Hub;

/// <summary>Liquipedia's MediaWiki API for the Brawlhalla wiki, on its terms (liquipedia.net/api-terms-of-use): at
/// most one request every 2 seconds and one parse every 30, gzip, one reused connection, a User-Agent naming this project
/// with a contact (the releases repo, never a person), the API only (never its HTML pages), and every answer cached for
/// as long as it's useful (the collector keeps one for 12 hours). Its content is CC-BY-SA 3.0: the app credits it.</summary>
public sealed class Liquipedia : IDisposable
{
    public const string Api = "https://liquipedia.net/brawlhalla/api.php";
    public const string UserAgent = "BHTracker-HubCollector/1.0 (https://github.com/duxvy/BHTracker-releases)";
    public static readonly TimeSpan RequestGap = TimeSpan.FromSeconds(2.5);
    public static readonly TimeSpan ParseGap = TimeSpan.FromSeconds(32);

    readonly HttpClient _http;
    readonly TimeProvider _time;
    readonly Func<TimeSpan, Task> _wait;
    DateTimeOffset? _lastRequest, _lastParse;

    public Liquipedia(HttpMessageHandler? handler = null, TimeProvider? time = null, Func<TimeSpan, Task>? wait = null)
    {
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.GZip })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _time = time ?? TimeProvider.System;
        _wait = wait ?? (d => Task.Delay(d));
    }

    /// <summary>The HTML the API makes of a piece of wikitext (action=parse).</summary>
    public async Task<string> ParseAsync(string wikitext, CancellationToken ct)
    {
        await WaitTurnAsync(parse: true);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["action"] = "parse", ["text"] = wikitext, ["contentmodel"] = "wikitext", ["prop"] = "text",
            ["disablelimitreport"] = "1", ["format"] = "json", ["formatversion"] = "2",
        });
        using var answer = await _http.PostAsync(Api, form, ct);
        answer.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("parse").GetProperty("text").GetString() ?? "";
    }

    /// <summary>The year's tournament list as the wiki shows it, the latest to end first.</summary>
    public Task<string> TournamentsAsync(int year, CancellationToken ct) =>
        ParseAsync($"{{{{TournamentsList|year={year}|order=enddate desc|limit=50}}}}", ct);

    async Task WaitTurnAsync(bool parse)
    {
        var now = _time.GetUtcNow();
        var wait = TimeSpan.Zero;
        if (_lastRequest is { } r && now - r < RequestGap) wait = RequestGap - (now - r);
        if (parse && _lastParse is { } p && now - p < ParseGap && ParseGap - (now - p) > wait) wait = ParseGap - (now - p);
        if (wait > TimeSpan.Zero) await _wait(wait);
        _lastRequest = _time.GetUtcNow();
        if (parse) _lastParse = _lastRequest;
    }

    public void Dispose() => _http.Dispose();
}
