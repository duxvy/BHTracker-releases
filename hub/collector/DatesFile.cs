using System.Globalization;
using System.Text.Json;

namespace BHTracker.Hub;

/// <summary>dates.json: the dates the owner approved for the Hub's countdowns (the /hub-dates skill adds them; the
/// collector never writes it). Each: a name, a kind, a start and/or an end, and optionally a link.</summary>
public static class DatesFile
{
    public static readonly string[] Kinds = ["season", "battle-pass", "event", "esports", "other"];

    /// <summary>The file's entries and, for each one left out, why.</summary>
    public static (IReadOnlyList<HubDate> Dates, IReadOnlyList<string> Problems) Read(string json)
    {
        var dates = new List<HubDate>();
        var problems = new List<string>();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        foreach (var e in doc.RootElement.GetProperty("dates").EnumerateArray())
        {
            string? Text(string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;
            var date = new HubDate(Text("name") ?? "", Text("kind") ?? "", Text("start"), Text("end"), Text("url"));
            if (Problem(date) is { } problem) problems.Add($"{(date.Name.Length > 0 ? date.Name : "(no name)")}: {problem}");
            else dates.Add(date);
        }
        return (dates, problems);
    }

    static string? Problem(HubDate d)
    {
        if (d.Name.Length is 0 or > 60) return "a name of 1 to 60 letters is needed";
        if (!Kinds.Contains(d.Kind)) return $"kind must be one of {string.Join(", ", Kinds)}";
        if (d.Start is null && d.End is null) return "a start or an end is needed";
        if (d.Start is not null && When(d.Start) is null) return $"start \"{d.Start}\" isn't a date (2026-10-07) or a time (2026-10-07T17:00:00Z)";
        if (d.End is not null && When(d.End) is null) return $"end \"{d.End}\" isn't a date (2026-10-28) or a time (2026-10-28T17:00:00Z)";
        if (d.Start is not null && d.End is not null && When(d.End) < When(d.Start)) return "it ends before it starts";
        if (d.Url is not null && !(Uri.TryCreate(d.Url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)) return "the link must be https";
        return null;
    }

    /// <summary>A date (the day's start, UTC) or a time with its offset; null for anything else.</summary>
    public static DateTimeOffset? When(string text)
    {
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return text.Contains('T') && (text.EndsWith('Z') || text.LastIndexOfAny(['+', '-']) > text.IndexOf('T'))
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;
    }

    /// <summary>The moment an entry is over: its end (a date's whole day), else its start.</summary>
    public static DateTimeOffset Over(HubDate d) =>
        d.End is { } end ? When(end)!.Value.AddDays(end.Contains('T') ? 0 : 1) : When(d.Start!)!.Value.AddDays(d.Start!.Contains('T') ? 0 : 1);
}
