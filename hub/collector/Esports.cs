using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace BHTracker.Hub;

/// <summary>An official esports event coming up or on now: its name, its days, and where it was announced.</summary>
public sealed record EsportsEvent(string Name, DateOnly Start, DateOnly End, string Url, string Source);

/// <summary>A finished tournament's top two, from Liquipedia.</summary>
public sealed record EsportsResult(string Name, DateOnly Start, DateOnly End, string Winner, string? RunnerUp, string Url);

/// <summary>Official esports: events from Blue Mammoth's Steam posts, results from Liquipedia's tournament list.</summary>
public static partial class Esports
{
    /// <summary>The official events announced in recent posts that haven't ended by <paramref name="today"/>. A post
    /// names its event in its heading or headline; its days come from a date in the post ("Tune in November 13-15"), or
    /// the post's own day when the event "begins today" (a weekend event: that day and the two after).</summary>
    public static IReadOnlyList<EsportsEvent> FromPosts(IEnumerable<Post> posts, DateOnly today)
    {
        var events = new List<EsportsEvent>();
        foreach (var post in posts)
        {
            var day = DateOnly.FromDateTime(post.Date.UtcDateTime);
            if (day < today.AddDays(-60)) continue;
            var headings = Bbcode.Sections(post.Contents).Select(s => s.Heading).Where(h => h.Length > 0);
            var name = headings.Prepend(post.Title).Select(Official).FirstOrDefault(n => n is not null);
            if (name is null) continue;
            var text = Bbcode.Plain(post.Contents);
            (DateOnly Start, DateOnly End)? days = null;
            foreach (Match d in DatePhrases.In(text))
                if (Announces().IsMatch(Sentence(text, d.Index)) && DatePhrases.Days(d, day) is { } found) { days = found; break; }
            if (days is null && Today().IsMatch(post.Title + " " + text))
                days = (day, name.EndsWith("Weekend", StringComparison.Ordinal) || name.Contains("Championship") ? day.AddDays(2) : day);
            if (days is not { } span || span.End < today) continue;
            if (events.Any(e => e.Name == name && e.Start == span.Start)) continue;
            events.Add(new(name, span.Start, span.End, post.Url, "steam"));
        }
        return events.OrderBy(e => e.Start).ToList();
    }

    /// <summary>The official event a heading or headline names, in its usual words, or null.</summary>
    public static string? Official(string text)
    {
        var m = OfficialName().Match(text);
        if (!m.Success) return null;
        var name = Regex.Replace(m.Value, @"\s+", " ").Trim();
        return name.StartsWith("Brawlhalla ", StringComparison.Ordinal) && !name.Contains("World") ? name[11..] : name;
    }

    // the sentence a date sits in
    static string Sentence(string text, int at)
    {
        int start = Math.Max(text.LastIndexOfAny(['.', '!', '?'], Math.Max(0, at - 1)) + 1, Math.Max(0, at - 200));
        int end = text.IndexOfAny(['.', '!', '?'], at);
        return text[start..(end < 0 ? text.Length : end)];
    }

    /// <summary>Finished official tournaments with a winner from a Liquipedia tournament list (the API's parse of
    /// {{TournamentsList}}), ended within <paramref name="days"/> before <paramref name="today"/>, newest first.</summary>
    public static IReadOnlyList<EsportsResult> FromLiquipedia(string html, DateOnly today, int days = 60)
    {
        html = WebUtility.HtmlDecode(html);
        var results = new List<EsportsResult>();
        foreach (Match row in Row().Matches(html))
        {
            var cells = Cell().Matches(row.Groups[1].Value).Select(c => (Class: c.Groups["class"].Value, Html: c.Groups["html"].Value)).ToList();
            var tournament = cells.FirstOrDefault(c => c.Class.Contains("column__tournament"));
            if (tournament.Html is null || Link().Match(tournament.Html) is not { Success: true } link) continue;
            var name = Text(link.Groups["text"].Value);
            if (Official(name) is null) continue;
            var dates = cells.Select(c => DateRange().Match(Text(c.Html))).FirstOrDefault(m => m.Success);
            if (dates is null || LiquipediaDays(dates) is not { } span) continue;
            var placements = cells.Where(c => c.Class.Contains("column__placement")).Select(c => Players(c.Html)).ToList();
            if (placements.Count == 0 || placements[0] is null) continue;
            if (span.End > today || span.End < today.AddDays(-days)) continue;
            var url = "https://liquipedia.net" + link.Groups["href"].Value;
            results.Add(new(name, span.Start, span.End, placements[0]!, placements.Count > 1 ? placements[1] : null, url));
        }
        return results.OrderByDescending(r => r.End).ToList();
    }

    // a placement cell's player (or a team's players, "A & B"); null for "TBD"
    static string? Players(string html)
    {
        var names = PlayerName().Matches(html).Select(m => Text(m.Groups[1].Value)).Where(n => n.Length > 0).ToList();
        return names.Count == 0 || names.All(n => n == "TBD") ? null : string.Join(" & ", names);
    }

    static (DateOnly Start, DateOnly End)? LiquipediaDays(Match m)
    {
        int year = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
        if (DatePhrases.Month(m.Groups["m1"].Value) is not { } m1) return null;
        int d1 = int.Parse(m.Groups["d1"].Value, CultureInfo.InvariantCulture);
        int m2 = m.Groups["m2"].Success && DatePhrases.Month(m.Groups["m2"].Value) is { } mm ? mm : m1;
        int d2 = m.Groups["d2"].Success ? int.Parse(m.Groups["d2"].Value, CultureInfo.InvariantCulture) : d1;
        try { return (new DateOnly(year, m1, d1), new DateOnly(m2 < m1 ? year + 1 : year, m2, d2)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    static string Text(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();

    [GeneratedRegex(@"\b(?:tune in|begins?|starts?|kicks? off|takes? place|runs?|live|returns?|held)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Announces();

    [GeneratedRegex(@"\b(?:begins?|starts?|kicks? off|is live|goes live) today\b", RegexOptions.IgnoreCase)]
    private static partial Regex Today();

    [GeneratedRegex(@"(?:Brawlhalla )?(?:World Championship(?: \d{4})?|BCX(?: \d{4})?|Midseason Championship(?: \d{4})?|(?:Winter|Spring|Summer|Autumn|Fall) Championship(?: \d{4})?(?: (?:Singles|Doubles))?(?: Weekend)?|Eternal Sports(?: [A-Z][\w-]*){1,3} Championships?(?: Weekend)?|Community Tournaments?(?: Weekend)?)")]
    private static partial Regex OfficialName();

    [GeneratedRegex(@"<tr class=""table2__row--body"">(.*?)</tr>", RegexOptions.Singleline)]
    private static partial Regex Row();

    [GeneratedRegex(@"<td(?: class=""(?<class>[^""]*)"")?[^>]*>(?<html>.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex Cell();

    [GeneratedRegex(@"<a href=""(?<href>/brawlhalla/[^""]+)""[^>]*>(?<text>.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex Link();

    [GeneratedRegex(@"<span class=""name""[^>]*>(.*?)</span>", RegexOptions.Singleline)]
    private static partial Regex PlayerName();

    [GeneratedRegex(@"(?<m1>[A-Z][a-z]{2}) (?<d1>\d{1,2})(?:\s*[–-]\s*(?:(?<m2>[A-Z][a-z]{2}) )?(?<d2>\d{1,2}))?, (?<y>\d{4})")]
    private static partial Regex DateRange();
}
