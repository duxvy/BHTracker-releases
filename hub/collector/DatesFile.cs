using System.Globalization;
using System.Text.Json;

namespace BHTracker.Hub;

/// <summary>dates.json: the dates the owner approved for the Hub's countdowns (the /hub-dates skill adds them; the
/// collector never writes it). Each: a name, a kind, a start and/or an end, and optionally a link. Its "removed" list
/// names the automatic countdowns (<see cref="AutoDates"/>) and estimates (<see cref="Estimates"/>) the owner took out,
/// each with its year; its "resets" what resets on a clock.</summary>
public static class DatesFile
{
    public static readonly string[] Kinds = ["season", "battle-pass", "event", "esports", "other"];

    /// <summary>hub.json's source for the owner's entries.</summary>
    public const string Source = "dates";

    /// <summary>The automatic countdowns the owner took out and, for each one left out, why.</summary>
    public static (IReadOnlyList<DateRemoval> Removed, IReadOnlyList<string> Problems) ReadRemoved(string json)
    {
        var removed = new List<DateRemoval>();
        var problems = new List<string>();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("removed", out var list)) return (removed, problems);
        if (list.ValueKind != JsonValueKind.Array) return (removed, ["removed: a list of { \"name\", \"year\" } is needed"]);
        foreach (var e in list.EnumerateArray())
        {
            bool entry = e.ValueKind == JsonValueKind.Object;
            var name = entry && e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : "";
            var year = entry && e.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number && y.TryGetInt32(out var number) ? number : 0;
            if (name.Length is 0 or > 60 || year is < 2014 or > 2100) problems.Add($"removed {(name.Length > 0 ? name : "(no name)")}: a name and a year (2026) are needed");
            else removed.Add(new DateRemoval(name, year));
        }
        return (removed, problems);
    }

    /// <summary>"day" or a weekday: how often a reset comes.</summary>
    public static readonly string[] Everies = ["day", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    /// <summary>What resets on a clock ("resets": a name, every, at, zone), each checked by the owner in the game, and, for
    /// each one left out, why. The zone is "UTC" or a region's IANA name ("America/New_York"): the app finds it.</summary>
    public static (IReadOnlyList<HubReset> Resets, IReadOnlyList<string> Problems) ReadResets(string json)
    {
        var resets = new List<HubReset>();
        var problems = new List<string>();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("resets", out var list)) return (resets, problems);
        if (list.ValueKind != JsonValueKind.Array) return (resets, ["resets: a list of { \"name\", \"every\", \"at\", \"zone\" } is needed"]);
        foreach (var e in list.EnumerateArray())
        {
            string Text(string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
            var reset = new HubReset(Text("name"), Text("every").ToLowerInvariant(), Text("at"), Text("zone"));
            if (ResetProblem(reset) is { } problem) problems.Add($"reset {(reset.Name.Length > 0 ? reset.Name : "(no name)")}: {problem}");
            else resets.Add(reset);
        }
        return (resets, problems);
    }

    static string? ResetProblem(HubReset r)
    {
        if (r.Name.Length is 0 or > 60) return "a name of 1 to 60 letters is needed";
        if (!Everies.Contains(r.Every)) return "every must be day or a weekday (wednesday)";
        if (!TimeOnly.TryParseExact(r.At, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return $"at \"{r.At}\" isn't a time (05:00)";
        if (!KnownZone(r.Zone)) return $"zone \"{r.Zone}\" isn't UTC or a region's name this check knows (America/New_York)";
        return null;
    }

    // the zone found by name (Linux, where the workflow runs, knows every region); on Windows the collector's invariant
    // globalization can't look region names up, so the usual ones are listed
    static readonly string[] Zones = ["UTC", "America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles", "Europe/London", "Europe/Paris", "Europe/Berlin"];

    static bool KnownZone(string zone) => Zones.Contains(zone) || TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _) && zone.Contains('/');

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
