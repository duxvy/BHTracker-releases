using System.Text.RegularExpressions;

namespace BHTracker.Hub;

/// <summary>A date the collector found in a post, for the owner to approve into dates.json (never added by itself):
/// a name from the known patterns, its days, and the post it came from.</summary>
public sealed record DateSuggestion(string Name, string Kind, DateOnly? Start, DateOnly? End, string Post, string Url);

/// <summary>Finds season, Battle Pass, event and esports dates in recent official posts. Only names (from fixed patterns
/// and the event list) and dates are kept, never the post's words.</summary>
public static partial class DateSuggestions
{
    public static IReadOnlyList<DateSuggestion> Find(IEnumerable<Post> posts, DateOnly today)
    {
        var found = new List<DateSuggestion>();
        foreach (var post in posts.Where(p => DateOnly.FromDateTime(p.Date.UtcDateTime) >= today.AddDays(-60)))
        {
            var posted = DateOnly.FromDateTime(post.Date.UtcDateTime);
            var text = Bbcode.Plain(post.Contents);
            foreach (var sentence in Sentence().Split(text))
                foreach (Match d in DatePhrases.In(sentence))
                {
                    if (Name(sentence, post.Title) is not { } name) continue;
                    if (DatePhrases.Days(d, posted) is not { } days) continue;
                    // "from October 7 through 28" is a span; "ends"/"until" an end; anything else a start
                    var (start, end) = days.Start != days.End ? ((DateOnly?)days.Start, (DateOnly?)days.End)
                        : Ends().IsMatch(sentence) ? (null, days.End) : (days.Start, (DateOnly?)null);
                    if ((end ?? start) < today) continue;
                    var s = new DateSuggestion(name.Name, name.Kind, start, end, post.Title, post.Url);
                    if (!found.Any(f => f.Name == s.Name && f.Start == s.Start && f.End == s.End)) found.Add(s);
                }
        }
        return found.OrderBy(f => f.Start ?? f.End).ToList();
    }

    // the thing a sentence dates, by our patterns: a Battle Pass, a ranked season, an esports event, an event on the list
    static (string Name, string Kind)? Name(string sentence, string title)
    {
        if (BattlePass().Match(sentence) is { Success: true } bp) return ($"Battle Pass {bp.Groups[1].Value}", "battle-pass");
        if (sentence.Contains("Battle Pass", StringComparison.OrdinalIgnoreCase) && BattlePass().Match(title) is { Success: true } tbp)
            return ($"Battle Pass {tbp.Groups[1].Value}", "battle-pass");
        if (RankedSeason().Match(sentence) is { Success: true } rs) return ($"Ranked Season {rs.Groups[1].Value}", "season");
        if (Esports.Official(sentence) is { } esports) return (esports, "esports");
        // "Tune in November 13-15" in a post whose headline names the event
        if (TuneIn().IsMatch(sentence) && Esports.Official(title) is { } titled) return (titled, "esports");
        if (Anniversary().Match(sentence) is { Success: true } an) return ($"{an.Value} Event", "event");
        foreach (var e in Names.Events)
            if (Regex.IsMatch(sentence, $@"(?<!\w){Regex.Escape(e)}(?!\w)", RegexOptions.IgnoreCase)) return (e, "event");
        return null;
    }

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex Sentence();

    [GeneratedRegex(@"\b(?:ends?|ending|until|last day|closes?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Ends();

    [GeneratedRegex(@"Battle Pass (Season \d+|Classic \d+)")]
    private static partial Regex BattlePass();

    [GeneratedRegex(@"\b(?:tune in|begins?|starts?|takes? place|kicks? off)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TuneIn();

    [GeneratedRegex(@"Ranked Season (\d+)")]
    private static partial Regex RankedSeason();

    [GeneratedRegex(@"\b\d+(?:st|nd|rd|th) Anniversary\b")]
    private static partial Regex Anniversary();
}
