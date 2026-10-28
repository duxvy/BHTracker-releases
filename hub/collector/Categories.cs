using System.Text.RegularExpressions;

namespace BHTracker.Hub;

/// <summary>What a post is about, for the Hub's News filters: "patch", "event", "crossover", "battle-pass", "esports" -
/// as many as fit - or "other". From its headline and headings, by the same lists and patterns as the summaries.</summary>
public static partial class Categories
{
    public const string Patch = "patch", Event = "event", Crossover = "crossover", BattlePass = "battle-pass", Esports = "esports", Other = "other";

    public static IReadOnlyList<string> For(string title, string bbcode, Names names)
    {
        var found = new List<string>();
        if (Summaries.PatchName(title) is not null || Summaries.IsBalancePost(title, bbcode, names)) found.Add(Patch);
        if (EventWords().IsMatch(title) || Names.Events.Any(e => Regex.IsMatch(title, $@"(?<!\w){Regex.Escape(e)}(?!\w)", RegexOptions.IgnoreCase)))
            found.Add(Event);
        if (CrossoverWords().IsMatch(title)) found.Add(Crossover);
        if (title.Contains("Battle Pass", StringComparison.OrdinalIgnoreCase)) found.Add(BattlePass);
        var headings = Bbcode.Sections(bbcode).Select(s => s.Heading).Where(h => h.Length > 0).Prepend(title);
        if (EsportsWords().IsMatch(title) || headings.Any(h => BrawlhallaEsports(h))) found.Add(Esports);
        if (found.Count == 0) found.Add(Other);
        return found;
    }

    static bool BrawlhallaEsports(string text) => BHTracker.Hub.Esports.Official(text) is not null;

    [GeneratedRegex(@"\b(?:Event|Anniversary)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EventWords();

    [GeneratedRegex(@"\bCrossover\b|\bBrawlhalla:? x\b", RegexOptions.IgnoreCase)]
    private static partial Regex CrossoverWords();

    [GeneratedRegex(@"\b(?:Esports|Championships?|BCX|Tournaments?|Twitch Drops|Pro Series|Circuit)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EsportsWords();
}
