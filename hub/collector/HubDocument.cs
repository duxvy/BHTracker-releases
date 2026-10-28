using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BHTracker.Hub;

/// <summary>hub.json, version 1: everything BHTracker's Hub shows. Only BHTracker's own summaries, facts, names, dates
/// and links: never a sentence of anyone's post. The app reads it with its own copy of this shape
/// (BrawlScout.Core/Hub/HubFeed.cs); a test in the app's repo keeps the two in step.</summary>
/// <param name="Updated">When the content last changed: a run that finds nothing new leaves the file as it is.</param>
public sealed record HubDocument(
    int Version,
    DateTimeOffset Updated,
    IReadOnlyList<HubNews> News,
    IReadOnlyList<HubPatch> Patches,
    HubEsports Esports,
    IReadOnlyList<HubDate> Countdowns)
{
    public const int CurrentVersion = 1;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // "→", "·" and accents as they are; it's never HTML
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json) + "\n";

    public static HubDocument? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<HubDocument>(json, Json) is { Version: CurrentVersion } d ? d : null; }
        catch (JsonException) { return null; }
    }
}

/// <summary>An official post: its headline, time, our one-line summary ("" when the lists name nothing in it) and link.</summary>
public sealed record HubNews(string Id, string Title, DateTimeOffset Date, string Summary, string Url);

/// <summary>A patch's balance changes by legend and weapon, with the link to the full notes.</summary>
public sealed record HubPatch(string Name, string Title, DateTimeOffset Date, string Url, IReadOnlyList<PatchSubject> Subjects);

/// <summary>Official events coming up (from Blue Mammoth's posts and the approved dates), and recent results
/// (Liquipedia, CC-BY-SA 3.0: the app credits it wherever they show).</summary>
public sealed record HubEsports(IReadOnlyList<EsportsEvent> Upcoming, IReadOnlyList<EsportsResult> Results)
{
    /// <summary>The results' source and licence, in the file itself too (CC-BY-SA asks for it wherever they're shared).</summary>
    public string Credit => "Results: data from Liquipedia (https://liquipedia.net/brawlhalla/), CC-BY-SA 3.0 (https://creativecommons.org/licenses/by-sa/3.0/)";
}

/// <summary>An approved date to count down to: a season, a Battle Pass, an event or an esports event. Start and End are
/// dates ("2026-10-07") or times with their offset ("2026-10-07T17:00:00Z"), as written in dates.json.</summary>
public sealed record HubDate(string Name, string Kind, string? Start, string? End, string? Url);
