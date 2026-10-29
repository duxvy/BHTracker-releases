using System.Globalization;
using System.Text.RegularExpressions;

namespace BHTracker.Hub;

/// <summary>Dates as posts write them: "November 13-15", "October 7 through 28", "Sept. 23".</summary>
public static partial class DatePhrases
{
    public static MatchCollection In(string text) => Phrase().Matches(text);

    /// <summary>The days a date phrase gives, in the year nearest the post's day (a January date in a December post is
    /// next year's). Null for a day that doesn't exist or a span that runs backwards.</summary>
    public static (DateOnly Start, DateOnly End)? Days(Match d, DateOnly posted)
    {
        if (Month(d.Groups["m"].Value) is not { } m1 || !int.TryParse(d.Groups["d"].Value, out var d1)) return null;
        int m2 = d.Groups["m2"].Success && Month(d.Groups["m2"].Value) is { } mm ? mm : m1;
        int d2 = d.Groups["d2"].Success && int.TryParse(d.Groups["d2"].Value, out var dd) ? dd : d1;
        int year = NearestYear(m1, posted);
        int year2 = m2 < m1 ? year + 1 : year;
        if (d1 < 1 || d2 < 1 || d1 > DateTime.DaysInMonth(year, m1) || d2 > DateTime.DaysInMonth(year2, m2)) return null;
        var (start, end) = (new DateOnly(year, m1, d1), new DateOnly(year2, m2, d2));
        return end < start ? null : (start, end);
    }

    /// <summary>The year nearest the post for a month: a January date in a December post is next year's, a December one
    /// in a January post last year's.</summary>
    public static int NearestYear(int month, DateOnly posted) =>
        posted.Year + (month < posted.Month - 6 ? 1 : month > posted.Month + 6 ? -1 : 0);

    /// <summary>A month's number from its name or three-letter short ("Sept" too), or null.</summary>
    public static int? Month(string name)
    {
        var key = name.TrimEnd('.').ToLowerInvariant();
        if (key == "sept") key = "sep";
        for (int i = 1; i <= 12; i++)
        {
            var full = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(i).ToLowerInvariant();
            if (full == key || (key.Length == 3 && full.StartsWith(key, StringComparison.Ordinal))) return i;
        }
        return null;
    }

    const string MonthWord = @"(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sept?(?:ember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?";

    [GeneratedRegex($@"\b(?<m>{MonthWord}) (?<d>\d{{1,2}})(?:st|nd|rd|th)?(?:\s*(?:-|–|through|to|until)\s*(?:(?<m2>{MonthWord}) )?(?<d2>\d{{1,2}})(?:st|nd|rd|th)?)?\b")]
    private static partial Regex Phrase();
}
