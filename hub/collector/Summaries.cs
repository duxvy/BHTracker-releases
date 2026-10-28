using System.Text.RegularExpressions;

namespace BHTracker.Hub;

/// <summary>A post's one-line summary in BHTracker's fixed format: names found on the bundled lists and counts, joined
/// by " · " ("New legend: Qinghua & Baobao · Back to School event · 23 balance changes · 5 bug fixes"). Empty when
/// nothing on the lists is named: the app then shows the headline and link alone.</summary>
public static partial class Summaries
{
    public static string For(string title, string bbcode, Names names)
    {
        var bits = new List<string>();
        if (NewLegend().Match(title) is { Success: true } l && names.LegendIn(title[(l.Index + l.Length)..]) is { } legend
            && Names.Fold(title[(l.Index + l.Length)..].TrimStart()).StartsWith(Names.Fold(legend), StringComparison.Ordinal))
            bits.Add($"New legend: {legend}");
        if (BattlePass().Match(title) is { Success: true } bp) bits.Add($"Battle Pass {bp.Groups[1].Value}");
        if (Crossover(title, names) is { } x) bits.Add($"{x} crossover");
        if (Anniversary().Match(title) is { Success: true } an) bits.Add($"{an.Value} event");
        foreach (var e in Names.Events)
            if (Regex.IsMatch(title, $@"(?<!\w){Regex.Escape(e)}(?!\w)", RegexOptions.IgnoreCase))
                bits.Add(e.EndsWith(" Event", StringComparison.Ordinal) ? $"{e[..^6]} event" : $"{e} event");
        if (Ascended().Match(title) is { Success: true } a && names.LegendIn(a.Groups[1].Value) is { } ascended)
            bits.Add($"Ascended {ascended}");

        int balance = PatchNotes.CountLines(bbcode, names);
        int fixes = Bbcode.Sections(bbcode).Sum(s => s.Bullets.Count(PatchNotes.IsFix));
        if (IsPatch(title) || IsBalancePost(title, bbcode, names))
        {
            if (balance > 0) bits.Add($"{balance} balance {(balance == 1 ? "change" : "changes")}");
            if (fixes > 0) bits.Add($"{fixes} bug {(fixes == 1 ? "fix" : "fixes")}");
        }
        return string.Join(" · ", bits.Distinct());
    }

    /// <summary>"Patch 11.00" from a patch post's title (not a balance preview), else null.</summary>
    public static string? PatchName(string title) =>
        PatchNumber().Match(title) is { Success: true } m && !IsPreview(title) ? $"Patch {m.Groups[1].Value}" : null;

    /// <summary>A post with balance changes in it, whatever its headline: "Patch 11.00", or 2020-2022's "New Legend:
    /// Mako, the Shark" - two balance lines at least, and never a balance preview (the patch itself has them).</summary>
    public static bool IsBalancePost(string title, string bbcode, Names names) =>
        !IsPreview(title) && PatchNotes.CountLines(bbcode, names) >= 2;

    /// <summary>What a balance post is called in the Hub: "Patch 11.00"; "Patch 3.06" for an older "Update 3.06" or
    /// "- 10.03"; else "Balance update".</summary>
    public static string PatchLabel(string title) =>
        PatchName(title) ?? (OtherNumber().Match(title) is { Success: true } m ? $"Patch {m.Groups[1].Value}" : "Balance update");

    static bool IsPreview(string title) => title.Contains("Preview", StringComparison.OrdinalIgnoreCase);

    static bool IsPatch(string title) => PatchName(title) is not null;

    // a crossover skin on the list named in the title ("Shrek"), else the name after "Brawlhalla x"
    static string? Crossover(string title, Names names)
    {
        if (!title.Contains("Crossover", StringComparison.OrdinalIgnoreCase) && !BrawlhallaX().IsMatch(title)) return null;
        if (names.CrossoverIn(title) is { } skin) return skin;
        return BrawlhallaX().Match(title) is { Success: true } m ? m.Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex(@"New Legend:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex NewLegend();

    [GeneratedRegex(@"Battle Pass (Season \d+|Classic \d+)")]
    private static partial Regex BattlePass();

    [GeneratedRegex(@"\b\d+(?:st|nd|rd|th) Anniversary\b")]
    private static partial Regex Anniversary();

    [GeneratedRegex(@"Ascended ([A-Z][\w'&]+(?: [A-Z][\w']+)?)")]
    private static partial Regex Ascended();

    [GeneratedRegex(@"Brawlhalla x ([A-Z][\w'.]*(?: [A-Z][\w'.]*){0,3})")]
    private static partial Regex BrawlhallaX();

    [GeneratedRegex(@"Patch\s+(\d+\.\d+)")]
    private static partial Regex PatchNumber();

    // "Update 3.06", "Patch Notes 9.13", "... - 10.03" at the end
    [GeneratedRegex(@"(?:Update|Patch Notes|[-–])\s*(\d+\.\d+)\b")]
    private static partial Regex OtherNumber();
}
