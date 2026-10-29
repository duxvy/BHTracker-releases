using System.Text.Json;

namespace BHTracker.Hub;

/// <summary>What one run has to work with. A null source failed this run: its part is kept from the previous hub.json.</summary>
/// <param name="Posts">Official Steam posts, newest first.</param>
/// <param name="LiquipediaPages">Liquipedia's tournament lists (the API's HTML), this year's first.</param>
/// <param name="History">The history files' index as this run left them (null: as they were).</param>
/// <param name="Removed">The automatic countdowns the owner took out (dates.json's "removed").</param>
public sealed record HubInputs(
    IReadOnlyList<Post>? Posts,
    IReadOnlyList<string>? LiquipediaPages,
    IReadOnlyList<HubDate> Dates,
    HubDocument? Previous,
    DateTimeOffset Now,
    HistoryIndex? History = null,
    IReadOnlyList<DateRemoval>? Removed = null);

/// <summary>Builds hub.json and the date suggestions from a run's inputs, and writes each file only when its content
/// changed (so the workflow commits only then).</summary>
public static class Collector
{
    public const int NewsCount = 12;
    public const int PatchCount = 3;
    public const int ResultCount = 6;
    const int PostsLooked = 40;

    public static HubDocument Build(HubInputs inputs, Names names)
    {
        var today = DateOnly.FromDateTime(inputs.Now.UtcDateTime);
        // a day's events and countdowns are kept a day after it ends in UTC: the app counts in the player's own days (an
        // American evening is already tomorrow in UTC) and hides what's over by its own clock
        var kept = today.AddDays(-1);
        var previous = inputs.Previous;
        var posts = inputs.Posts;

        var news = posts?.Take(NewsCount).Select(p => History.News(p, names)).ToList()
            ?? previous?.News.ToList() ?? [];
        var patches = posts?.Select(p => History.Patch(p, names)).OfType<HubPatch>().Take(PatchCount).ToList()
            ?? previous?.Patches.ToList() ?? [];

        var announced = posts is null
            ? previous?.Esports.Upcoming.Where(e => e.Source == "steam" && e.End >= kept).ToList() ?? []
            : Esports.FromPosts(posts.Take(PostsLooked), kept).ToList();
        var approved = inputs.Dates.Where(d => d.Kind == "esports" && DatesFile.Over(d).AddDays(1) > inputs.Now)
            .Select(d => new EsportsEvent(d.Name, Day(d.Start ?? d.End!), Day(d.End ?? d.Start!), d.Url ?? "", "dates"));
        var upcoming = approved.Concat(announced)
            .GroupBy(e => (Names.Fold(e.Name), e.Start)).Select(g => g.First())   // the owner's own entry wins
            .OrderBy(e => e.Start).ThenBy(e => e.Name, StringComparer.Ordinal).ToList();

        var results = inputs.LiquipediaPages is { } pages
            ? pages.SelectMany(p => Esports.FromLiquipedia(p, today)).DistinctBy(r => r.Url).OrderByDescending(r => r.End).Take(ResultCount).ToList()
            : previous?.Esports.Results.Where(r => r.End >= today.AddDays(-60)).ToList() ?? [];

        var countdowns = inputs.Dates.Select(d => d with { Source = DatesFile.Source }).Concat(Automatic(inputs))
            .Where(d => DatesFile.Over(d).AddDays(1) > inputs.Now)
            .OrderBy(d => DatesFile.When(d.Start ?? d.End!)).ToList();

        return new HubDocument(HubDocument.CurrentVersion, previous?.Updated ?? inputs.Now, news, patches,
            new HubEsports(upcoming, results), countdowns, inputs.History ?? previous?.History);
    }

    static DateOnly Day(string when) => DateOnly.FromDateTime(DatesFile.When(when)!.Value.UtcDateTime);

    /// <summary>The countdowns official posts give for certain (<see cref="AutoDates"/>; the last run's when Steam
    /// failed), less any the owner took out or set themselves in dates.json: the owner's always win.</summary>
    public static IReadOnlyList<HubDate> Automatic(HubInputs inputs)
    {
        var found = inputs.Posts is { } posts
            ? AutoDates.Find(posts.Take(PostsLooked), DateOnly.FromDateTime(inputs.Now.UtcDateTime).AddDays(-1))
            : inputs.Previous?.Countdowns.Where(c => c.Source == AutoDates.Source).ToList() ?? [];
        return found.Where(a => !Owners(inputs, a.Name, Years(a))
            && !inputs.Dates.Any(d => d.Kind == a.Kind && d.Start == a.Start && d.End == a.End)).ToList();
    }

    /// <summary>Dates found in recent posts that aren't counted down to yet, for the owner to look at (one they took out
    /// is still suggested: a later post may give it again).</summary>
    public static IReadOnlyList<DateSuggestion> Suggest(HubInputs inputs)
    {
        if (inputs.Posts is null) return [];
        var automatic = Automatic(inputs);
        return DateSuggestions.Find(inputs.Posts.Take(PostsLooked), DateOnly.FromDateTime(inputs.Now.UtcDateTime))
            .Where(s => !inputs.Dates.Any(d => Names.Fold(d.Name) == Names.Fold(s.Name))
                && !automatic.Any(a => AutoDates.Key(a.Name) == AutoDates.Key(s.Name)))
            .ToList();
    }

    // whether the owner's dates.json has its own entry for this thing in one of these years, or took it out
    static bool Owners(HubInputs inputs, string name, IEnumerable<int> years)
    {
        var key = AutoDates.Key(name);
        var them = years.ToList();
        return inputs.Dates.Any(d => AutoDates.Key(d.Name) == key && Years(d).Intersect(them).Any())
            || (inputs.Removed ?? []).Any(r => AutoDates.Key(r.Name) == key && them.Contains(r.Year));
    }

    static IEnumerable<int> Years(HubDate d) => new[] { d.Start, d.End }.OfType<string>().Select(w => DatesFile.When(w)!.Value.Year);

    /// <summary>Writes hub.json when anything but its time changed, stamping the new time; says whether it wrote.</summary>
    public static bool WriteHub(string path, HubDocument built, DateTimeOffset now)
    {
        var old = File.Exists(path) ? File.ReadAllText(path) : null;
        if (old is not null && HubDocument.FromJson(old) is { } before && (built with { Updated = before.Updated }).ToJson() == old)
            return false;
        WriteAtomically(path, (built with { Updated = now }).ToJson());
        return true;
    }

    /// <summary>Writes the suggestions file when its content changed; says whether it wrote.</summary>
    public static bool WriteSuggestions(string path, IReadOnlyList<DateSuggestion> suggestions)
    {
        var json = JsonSerializer.Serialize(new { note = SuggestionsNote, suggestions }, HubDocument.Json) + "\n";
        if (File.Exists(path) && File.ReadAllText(path) == json) return false;
        WriteAtomically(path, json);
        return true;
    }

    public const string SuggestionsNote =
        "Dates found in Brawlhalla's recent posts, for the owner to check. None of them shows in BHTracker until it's added to dates.json (the /hub-dates skill).";

    static void WriteAtomically(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}
