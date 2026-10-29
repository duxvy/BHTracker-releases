using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace BHTracker.Hub;

/// <summary>An automatic countdown the owner took out (dates.json's "removed"): that name, in that year, never shows by
/// itself again.</summary>
public sealed record DateRemoval(string Name, int Year);

/// <summary>The countdowns the collector adds by itself: only what an official post states for certain, in days, never a
/// guessed hour. A sentence (a post's headings, bullets and lines kept apart) names one thing on our lists - a Battle
/// Pass, a ranked season, an event, an official esports event - and gives one date with a month and a day: a span
/// ("October 7 through 28"), or one day the sentence says starts or ends it ("starts November 4", "until November 4"); an
/// esports event only as a span. A line about earning tickets belongs to the one event its headline names, "Tune in November 13-15" to
/// the one official event its headline names. The newest post that dates a thing decides ("extended until November
/// 4"); a post that dates it two ways, or a newer one that says it changed without a date ("extended") or gives it other
/// days ("runs through December" for one that ends in November), leaves it to the owner (the suggestions). Only the
/// name, the days and the post's link are kept.</summary>
public static partial class AutoDates
{
    public const string Source = "steam";

    // what one sentence says about one thing: its days; or, not for certain, the days and months it mentions, whether it
    // speaks of a change ("extended"), and whether it speaks of this one thing's own days (a start or end word)
    sealed record Sighting(string Name, string Kind, DateOnly? Start, DateOnly? End, Post Post,
        IReadOnlyList<(DateOnly From, DateOnly To)>? Mentions = null, bool Changed = false, bool Speaks = false)
    {
        public bool Certain => Start is not null || End is not null;
    }

    /// <summary>The countdowns the posts give for certain that aren't over by <paramref name="today"/>, each linking to
    /// the newest post that dates it.</summary>
    public static IReadOnlyList<HubDate> Find(IEnumerable<Post> posts, DateOnly today)
    {
        var found = new List<HubDate>();
        foreach (var thing in posts.SelectMany(p => Seen.GetValue(p, q => Sightings(q).ToList())).GroupBy(s => Key(s.Name)))
        {
            if (thing.Where(s => s.Certain).MaxBy(s => s.Post.Date) is not { } newest) continue;
            // this year's run of it: never last year's start with this year's end
            var certain = thing.Where(s => s.Certain && s.Post.Date > newest.Post.Date.AddDays(-180)).ToList();
            var start = Day(certain, s => s.Start);
            var end = Day(certain, s => s.End);
            if (start is { Sure: false } || end is { Sure: false } || start?.Day > end?.Day) continue;
            if ((end?.Day ?? start!.Value.Day) < today) continue;
            // a newer post that says it changed, or gives it other days: it may have moved
            if (thing.Any(s => !s.Certain && s.Post.Date > newest.Post.Date
                && (s.Changed || (s.Speaks && s.Mentions!.Any(m => m.To < start?.Day || m.From > end?.Day)))))
                continue;
            found.Add(new HubDate(newest.Name, newest.Kind, Text(start?.Day), Text(end?.Day), newest.Post.Url, Source));
        }
        return found;
    }

    /// <summary>A name as one thing, whatever the post calls it: "Brawlhalla World Championship 2026" and "World
    /// Championship" are one.</summary>
    public static string Key(string name) => KeyTrim().Replace(Names.Fold(name.Trim()), "") is var key && key == "bcx" ? "world championship" : key;

    // what each post says, read once (a run asks twice: for hub.json and for the suggestions)
    static readonly ConditionalWeakTable<Post, List<Sighting>> Seen = new();

    // a start or an end: from the newest post that gives one, unsure when that post gives two
    static (DateOnly Day, bool Sure)? Day(List<Sighting> certain, Func<Sighting, DateOnly?> day)
    {
        var giving = certain.Where(s => day(s) is not null).ToList();
        if (giving.Count == 0) return null;
        var latest = giving.Max(s => s.Post.Date);
        var days = giving.Where(s => s.Post.Date == latest).Select(s => day(s)!.Value).Distinct().ToList();
        return (days[0], days.Count == 1);
    }

    static string? Text(DateOnly? day) => day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static IEnumerable<Sighting> Sightings(Post post)
    {
        var posted = DateOnly.FromDateTime(post.Date.UtcDateTime);
        var headline = DateSuggestions.Things(post.Title);
        var headlineEvents = headline.Where(t => t.Kind == "event").ToList();
        var headlineEsports = headline.Where(t => t.Kind == "esports").ToList();
        foreach (var sentence in DateSuggestions.Sentences(post.Contents))
        {
            var dates = DatePhrases.In(sentence);
            if (dates.Count == 0 && !Month().IsMatch(sentence) && !Change().IsMatch(sentence)) continue;   // most: no date at all
            var things = DateSuggestions.Things(sentence);
            var rest = things.Aggregate(sentence, (s, t) => s.Replace(t.Name, "", StringComparison.OrdinalIgnoreCase));
            var months = Month().Matches(rest);
            bool changed = Change().IsMatch(rest);
            if (dates.Count == 0 && months.Count == 0 && !changed) continue;
            if (things.Count == 0)
            {
                if (Tickets().IsMatch(sentence) && headlineEvents.Count == 1) things = headlineEvents;
                else if (DateSuggestions.TuneIn().IsMatch(sentence) && headlineEsports.Count == 1) things = headlineEsports;
                else continue;
            }
            var (starts, ends) = (Starts().IsMatch(sentence), Ends().IsMatch(sentence));
            // of this one thing's own days (an esports event's single days are its sign-ups and drops, never its own)
            bool speaks = things.Count == 1 && things[0].Kind != "esports" && (starts || ends);
            // two things, two dates, no date, or a "no more tickets from ...": not for certain
            if (things.Count > 1 || dates.Count != 1 || Negative().IsMatch(sentence))
            {
                var mentions = dates.Select(d => DatePhrases.Days(d, posted)).OfType<(DateOnly Start, DateOnly End)>()
                    .Concat(months.Select(m => WholeMonth(DatePhrases.Month(m.Value)!.Value, posted))).ToList();
                foreach (var t in things) yield return new(t.Name, t.Kind, null, null, post, mentions, changed, speaks);
                continue;
            }
            var (name, kind) = things[0];
            if (DatePhrases.Days(dates[0], posted) is not { } d) continue;
            if (d.Start != d.End) yield return new(name, kind, d.Start, d.End, post);
            else if (kind == "esports" || starts == ends) yield return new(name, kind, null, null, post, [d], changed, speaks);
            else if (starts && kind == "season" && Before(name) is { } ending) yield return new(ending, kind, null, d.End, post);
            else yield return starts ? new(name, kind, d.Start, null, post) : new(name, kind, null, d.End, post);
        }
    }

    // a ranked season starts the day the one before it ends, and players count down to their season's end (the owner's
    // call): "Ranked Season 42 begins September 23" is Ranked Season 41 ending then
    static string? Before(string season) =>
        int.TryParse(season[(season.LastIndexOf(' ') + 1)..], out var n) && n > 1 ? $"Ranked Season {n - 1}" : null;

    // a month named without a day, as its first and last days, in the year nearest the post
    static (DateOnly, DateOnly) WholeMonth(int month, DateOnly posted)
    {
        var first = new DateOnly(DatePhrases.NearestYear(month, posted), month, 1);
        return (first, first.AddMonths(1).AddDays(-1));
    }

    [GeneratedRegex(@"^brawlhalla |\s+\d{4}$")]
    private static partial Regex KeyTrim();

    [GeneratedRegex(@"\b(?:begins?|beginning|start(?:s|ing)?|kicks? off|returns?|arriv(?:e|es|ing)|launch(?:es|ing)?|from)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Starts();

    [GeneratedRegex(@"\b(?:ends?|ending|until|till|through|last day|final day|closes?|expires?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Ends();

    // earning the event's tickets: its own days ("tickets will be earnable until September 9th"), never "unused Tickets
    // will be converted to Gold"
    [GeneratedRegex(@"^(?=.*\bearn\w*)(?=.*\b(?:Event Center|tickets?)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex Tickets();

    // a change: "extended", "delayed"
    [GeneratedRegex(@"\b(?:extend\w*|delay\w*|postpone\w*|reschedul\w*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Change();

    [GeneratedRegex(@"\b(?:no|not|no longer|never|cannot)\b|n't\b", RegexOptions.IgnoreCase)]
    private static partial Regex Negative();

    // a month without a day: "through November"
    [GeneratedRegex(@"\b(?:January|February|March|April|May|June|July|August|September|October|November|December|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sept?|Oct|Nov|Dec)\b(?!\.?\s*\d{1,2}(?!\d))")]
    private static partial Regex Month();
}
