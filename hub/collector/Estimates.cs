using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BHTracker.Hub;

/// <summary>When a Battle Pass (Classic ones too) or a ranked season started: a day, and the post that gave it.</summary>
public sealed record SeasonStart(string Name, string Kind, string Start, string? Url);

/// <summary>
/// The estimated end of the Battle Pass and the ranked season on now, while no post gives the real one: its start plus the
/// typical (median) length of the last <see cref="Recent"/> of its line, each lasting until the next one started (a
/// length with a start missing between - ranked numbers not one apart, or over <see cref="Longest"/> days - is left out).
/// Never shown when fewer than <see cref="Fewest"/> lengths are known, when they're more than <see cref="MostSpread"/> days
/// apart (too varied to say), when the next one's start or an end is known (that's the real date), when a post names the
/// next one (it may already be on: the owner adds its start), or once the estimated day has passed. Starts come from the
/// owner's hub/seasons.json, then dates.json, then the posts (a dated sentence, or a headline saying it begins), then the
/// ones earlier runs kept (season-starts.json on hub-data). Plain arithmetic on days; no post's words.
/// </summary>
public static partial class Estimates
{
    public const int Recent = 6, Fewest = 4, MostSpread = 21, Longest = 150;

    /// <summary>The file on hub-data where the collector keeps every start it has known (the posts only give the newest).</summary>
    public const string KeptFile = "season-starts.json";

    static readonly string[] Lines = ["battle-pass", "season"];

    /// <param name="ends">Every end known, over or not: the owner's dates and what the posts give for certain.</param>
    /// <param name="named">The Battle Passes and ranked seasons the recent posts name (<see cref="Named"/>).</param>
    /// <param name="notes">Why a line has no estimate when the owner can help (a start to add), for the run's log.</param>
    public static IReadOnlyList<HubEstimate> Find(IReadOnlyList<SeasonStart> starts, IEnumerable<HubDate> ends,
        IReadOnlyCollection<string> named, DateOnly today, List<string>? notes = null)
    {
        var found = new List<HubEstimate>();
        foreach (var kind in Lines)
        {
            var line = starts.Where(s => s.Kind == kind).Select(s => (s.Name, Day: Day(s.Start))).OrderBy(s => s.Day).ToList();
            int now = line.FindLastIndex(s => s.Day <= today);
            // none started yet, or the next one's start is known: its end is a real date
            if (now < 0 || now < line.Count - 1) continue;
            var current = line[now];
            if (named.FirstOrDefault(n => After(n, kind, line.Select(s => s.Name))) is { } next)
            {
                notes?.Add($"{next} is named in a recent post but its start isn't known, so {current.Name} has no estimate: add {next}'s start to hub/seasons.json (/hub-dates)");
                continue;
            }
            var lengths = Enumerable.Range(0, now).Select(i => (From: line[i], To: line[i + 1]))
                .Where(p => p.To.Day.DayNumber - p.From.Day.DayNumber <= Longest && (kind != "season" || Number(p.To.Name) == Number(p.From.Name) + 1))
                .Select(p => p.To.Day.DayNumber - p.From.Day.DayNumber).TakeLast(Recent).Order().ToList();
            if (lengths.Count < Fewest || lengths[^1] - lengths[0] > MostSpread)
            {
                notes?.Add($"{current.Name} has no estimate: {lengths.Count} recent lengths known{(lengths.Count > 0 ? $", {lengths[0]} to {lengths[^1]} days" : "")}");
                continue;
            }
            var end = current.Day.AddDays((lengths[(lengths.Count - 1) / 2] + lengths[lengths.Count / 2]) / 2);
            if (ends.Any(c => c.End is not null && AutoDates.Key(c.Name) == AutoDates.Key(current.Name))) continue;
            // shown only while it's shorter than any of them lasted: from then on it may already be over (kept a day past in
            // UTC, as countdowns are: the app hides it by the player's own clock)
            if (current.Day.AddDays(lengths[0]) < today)
            {
                if (end < today) notes?.Add($"{current.Name}'s estimated end ({Text(end)}) has passed and no newer start is known: add it to hub/seasons.json (/hub-dates)");
                continue;
            }
            found.Add(new HubEstimate(current.Name, kind, Text(current.Day), Text(end), lengths.Count, lengths[0], lengths[^1]));
        }
        return found;
    }

    // a name later in this line than any it has: Ranked Season 43 after 42; Battle Pass Classic 9 after Classic 8
    static bool After(string name, string kind, IEnumerable<string> line)
    {
        var (series, number) = Series(name);
        var same = line.Select(Series).Where(k => k.Series == series).ToList();
        return series.StartsWith("Ranked", StringComparison.Ordinal) == (kind == "season") && same.Count > 0 && number > same.Max(k => k.Number);
    }

    static (string Series, int Number) Series(string name)
    {
        int space = name.LastIndexOf(' ');
        return (name[..Math.Max(0, space)], int.TryParse(name[(space + 1)..], out var n) ? n : 0);
    }

    static int? Number(string season) =>
        season.StartsWith("Ranked Season ", StringComparison.Ordinal) && int.TryParse(season[14..], out var n) ? n : null;

    /// <summary>Every start known, each name once: the owner's list first, then their dates.json, then what the posts say
    /// for certain (a ranked season's end is the next one's start), then what earlier runs kept (a headline's start stays
    /// the first headline's once that post is no longer among the newest), then the headlines.</summary>
    /// <param name="removed">Names the owner says are no start at all (seasons.json's "removed"): never taken from the
    /// posts or kept.</param>
    public static IReadOnlyList<SeasonStart> Known(IEnumerable<SeasonStart> owners, IEnumerable<HubDate> dates,
        IEnumerable<HubDate> fromPosts, IEnumerable<SeasonStart> headlines, IEnumerable<SeasonStart> kept,
        IEnumerable<string>? removed = null)
    {
        var mine = owners.ToList();
        var gone = (removed ?? []).Select(AutoDates.Key).ToHashSet();
        return mine.Concat(Starts(dates).Concat(Starts(fromPosts)).Concat(kept).Concat(headlines).Where(s => !gone.Contains(AutoDates.Key(s.Name))))
            .Where(s => Lines.Contains(s.Kind))
            .DistinctBy(s => AutoDates.Key(s.Name))
            .OrderBy(s => s.Kind, StringComparer.Ordinal).ThenBy(s => s.Start, StringComparer.Ordinal).ToList();
    }

    /// <summary>What season-starts.json keeps: the starts learned, never one the owner's list has (so taking a line out
    /// of seasons.json takes it out everywhere, unless a post gives it again).</summary>
    public static IReadOnlyList<SeasonStart> Learned(IReadOnlyList<SeasonStart> known, IEnumerable<SeasonStart> owners)
    {
        var mine = owners.Select(s => AutoDates.Key(s.Name)).ToHashSet();
        return known.Where(s => !mine.Contains(AutoDates.Key(s.Name))).ToList();
    }

    /// <summary>seasons.json's "removed": names that are no start at all (a wrong one a post gave).</summary>
    public static IReadOnlyList<string> ReadRemoved(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return doc.RootElement.TryGetProperty("removed", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim()).Where(n => n.Length > 0).ToList()
            : [];
    }

    static IEnumerable<SeasonStart> Starts(IEnumerable<HubDate> dates)
    {
        foreach (var d in dates.Where(d => Lines.Contains(d.Kind)))
        {
            if (d.Start is { } start) yield return new SeasonStart(d.Name, d.Kind, Text(Day(start)), d.Url);
            else if (d.Kind == "season" && d.End is { } end && Number(d.Name) is { } n)
                yield return new SeasonStart($"Ranked Season {n + 1}", d.Kind, Text(Day(end)), d.Url);
        }
    }

    /// <summary>The first headline that names one Battle Pass or ranked season ("Battle Pass Classic 7 Begins!", "Battle Pass
    /// Season 11: Azoth's Ascension – Patch 9.04") starts it the day it's posted - never a reveal, a preview, what's coming or
    /// a last week. Its patch post now and then comes a few days early: the owner's list and any dated post win.</summary>
    public static IEnumerable<SeasonStart> FromHeadlines(IEnumerable<Post> posts) =>
        posts.Where(p => !NotYet().IsMatch(p.Title))
            .Select(p => (Post: p, Names: DateSuggestions.Seasons(p.Title).ToList()))
            .Where(h => h.Names.Count == 1)
            .GroupBy(h => h.Names[0]).Select(g => g.MinBy(h => h.Post.Date))
            .Select(h => new SeasonStart(h.Names[0].Name, h.Names[0].Kind, Text(DateOnly.FromDateTime(h.Post.Date.UtcDateTime)), h.Post.Url));

    /// <summary>The Battle Passes and ranked seasons these posts name, headline or text (each post read once).</summary>
    public static IReadOnlyCollection<string> Named(IEnumerable<Post> posts) =>
        posts.SelectMany(p => NamedIn.GetValue(p, q => DateSuggestions.Seasons(q.Title + "\n" + q.Contents).Select(s => s.Name).ToList()))
            .ToHashSet();

    static readonly ConditionalWeakTable<Post, List<string>> NamedIn = new();

    /// <summary>hub/seasons.json's or season-starts.json's starts; a malformed entry is a problem, never a stopped run.</summary>
    public static (IReadOnlyList<SeasonStart> Starts, IReadOnlyList<string> Problems) Read(string json)
    {
        var starts = new List<SeasonStart>();
        var problems = new List<string>();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!doc.RootElement.TryGetProperty("seasons", out var list) || list.ValueKind != JsonValueKind.Array) return (starts, ["seasons: a list is needed"]);
        foreach (var e in list.EnumerateArray())
        {
            string? Text(string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;
            var (name, kind, start, url) = (Text("name") ?? "", Text("kind") ?? "", Text("start") ?? "", Text("url"));
            if (name.Length is 0 or > 60 || !Lines.Contains(kind) || !DateOnly.TryParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                problems.Add($"{(name.Length > 0 ? name : "(no name)")}: a name, a kind (battle-pass or season) and a start (2026-09-16) are needed");
            else starts.Add(new SeasonStart(name, kind, start, url));
        }
        return (starts, problems);
    }

    /// <summary>Writes the kept starts when they changed; says whether it wrote.</summary>
    public static bool WriteKept(string path, IReadOnlyList<SeasonStart> starts)
    {
        var json = JsonSerializer.Serialize(new { note = KeptNote, seasons = starts }, HubDocument.Json) + "\n";
        if (File.Exists(path) && File.ReadAllText(path) == json) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".tmp", json);
        File.Move(path + ".tmp", path, overwrite: true);
        return true;
    }

    public const string KeptNote =
        "The Battle Pass and ranked season starts the Hub's collector learned from posts, beyond hub/seasons.json on main (the owner's list, which wins over anything here and whose removed list takes one out), for its estimated ends. Written by the collector.";

    static DateOnly Day(string when) => DateOnly.FromDateTime(DatesFile.When(when)!.Value.UtcDateTime);

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\b(?:reveal\w*|preview|coming|soon|tomorrow|next|ends?|ending|final|last)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotYet();
}
