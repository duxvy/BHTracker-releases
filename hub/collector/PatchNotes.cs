using System.Globalization;
using System.Text.RegularExpressions;

namespace BHTracker.Hub;

/// <summary>One change to a move: a fact in BHTracker's own words ("damage 12 → 14"), or null when the post's line can't
/// be restated as one (the app shows "changed"). Dir: 1 better, -1 worse, 0 not certain.</summary>
public sealed record PatchChange(string Move, string? Fact, int Dir);

/// <summary>A legend's or weapon's changes in a patch. Others: changes whose move can't be told.</summary>
public sealed record PatchSubject(string Name, string Kind, IReadOnlyList<PatchChange> Changes, int Others);

/// <summary>Reads a patch post's balance changes into facts. Never keeps the post's words: only names (legends,
/// weapons, moves), numbers, and BHTracker's own stat words.</summary>
public static partial class PatchNotes
{
    /// <summary>The balance changes in a post, by legend and weapon in the post's order.</summary>
    public static IReadOnlyList<PatchSubject> Read(string bbcode, Names names)
    {
        var subjects = new List<(string Name, List<PatchChange> Changes, int[] Others)>();
        (string Name, List<PatchChange> Changes, int[] Others) Get(string name)
        {
            var found = subjects.FindIndex(s => s.Name == name);
            if (found >= 0) return subjects[found];
            subjects.Add((name, [], [0]));
            return subjects[^1];
        }

        foreach (var section in Bbcode.Sections(bbcode))
        {
            var heading = names.Subject(section.Heading);
            foreach (var bullet in section.Bullets)
            {
                if (IsFix(bullet)) continue;
                var line = Line(bullet, heading, names);
                if (line is not { } l) continue;
                var subject = Get(l.Subject);
                if (l.Move is null)
                {
                    if (Balance().IsMatch(bullet)) subject.Others[0]++;
                    continue;
                }
                bool vague = false;
                foreach (var clause in Clauses(l.Changes))
                    if (Fact(clause) is { } f) subject.Changes.Add(new(l.Move, f.Text, f.Dir));
                    else vague = true;
                if (vague && !subject.Changes.Any(c => c.Move == l.Move && c.Fact is null))
                    subject.Changes.Add(new(l.Move, null, 0));
            }
        }
        return subjects.Where(s => s.Changes.Count > 0 || s.Others[0] > 0)
            .Select(s => new PatchSubject(s.Name, names.KindOf(s.Name)!, s.Changes, s.Others[0])).ToList();
    }

    /// <summary>How many lines of a post are balance changes to a move ("Hattori Side Sword: ...").</summary>
    public static int CountLines(string bbcode, Names names) =>
        Bbcode.Sections(bbcode).Sum(s => s.Bullets.Count(b => !IsFix(b) && Line(b, names.Subject(s.Heading), names) is { Move: not null }));

    /// <summary>A bug fix: "Fixed a bug where ...", or a move's "Vivi Neutral Blasters: Fixed a case where ...".</summary>
    public static bool IsFix(string bullet)
    {
        if (bullet.StartsWith("Fixed", StringComparison.OrdinalIgnoreCase)) return true;
        int colon = bullet.IndexOf(':');
        return colon > 0 && colon <= 60 && bullet[(colon + 1)..].TrimStart().StartsWith("Fixed ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A bullet's legend or weapon, its move and the text of its changes. The bullet's own words name the
    /// subject first ("Greatsword Down Light Opener: ..." is the Greatsword's, whatever heading it sits under); a bare
    /// move ("Neutral Blasters: ...") belongs to the heading's legend or weapon. Null: not about a legend or weapon. A
    /// null move: about the heading's subject, but no move can be told.</summary>
    static (string Subject, string? Move, string Changes)? Line(string bullet, string? heading, Names names)
    {
        int colon = bullet.IndexOf(':');
        if (colon > 0 && colon <= 60)
        {
            var head = bullet[..colon].Trim();
            var changes = bullet[(colon + 1)..].Trim();
            if (names.StartsWith(head) is { } own && IsMove(own.Move)) return (own.Subject, own.Move, changes);
            if (heading is not null && IsMove(head)) return (heading, head, changes);
        }
        return heading is null ? null : (heading, null, bullet);
    }

    /// <summary>A move's name, as patch notes write them: "Side Light", "Fused Side Air", "Down Light Finisher (from Side
    /// Light Bridge)" - five words at most, and five in brackets after it. Never a sentence: a move is a name kept in
    /// hub.json, so anything longer isn't one.</summary>
    public static bool IsMove(string text)
    {
        if (!Move().IsMatch(text)) return false;
        int bracket = text.IndexOf('(');
        int Words(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return bracket < 0 ? Words(text) <= 5 : Words(text[..bracket]) <= 5 && Words(text[bracket..]) <= 5;
    }

    static IEnumerable<string> Clauses(string changes) =>
        ClauseSplit().Split(changes).Select(c => c.Trim().TrimEnd('.').Trim()).Where(c => c.Length > 0);

    /// <summary>A clause restated as a fact in our words, or null. "Increased/Decreased X from A to B", the older "X
    /// increased from A to B", or "Decreased X by 32%", for a stat we name; the direction only where it's certain.</summary>
    public static (string Text, int Dir)? Fact(string clause)
    {
        clause = clause.Trim().TrimEnd('.').Trim();
        if (PercentPattern().Match(clause) is { Success: true } pc) return Percent(pc);
        var m = FactPattern().Match(clause);
        if (!m.Success) m = InvertedPattern().Match(clause);
        if (!m.Success) return null;
        var (stat, qualifiers) = Stat(m.Groups["stat"].Value);
        if (stat is null) return null;
        var (a, b) = (m.Groups["a"].Value, m.Groups["b"].Value);
        var na = Numbers(a);
        var nb = Numbers(b);
        // every number one way (pair by pair, or one number against a range), else not certain
        IEnumerable<int> moves = na.Count == nb.Count ? na.Zip(nb, (x, y) => Math.Sign(y - x))
            : na.Count == 1 ? nb.Select(y => Math.Sign(y - na[0]))
            : nb.Count == 1 ? na.Select(x => Math.Sign(nb[0] - x)) : [1, -1];
        var signs = moves.Where(s => s != 0).Distinct().ToList();
        int sign = signs.Count == 1 ? signs[0] : 0;
        int dir = BetterWhenHigher(stat) switch { true => sign, false => -sign, null => 0 };
        var detail = qualifiers.Count == 0 ? "" : $" ({string.Join(", ", qualifiers)})";
        return ($"{stat}{detail} {Value(a)} → {Value(b)}", dir);
    }

    // "Decreased Variable Force by 32%" → "force (variable) -32%"
    static (string Text, int Dir)? Percent(Match m)
    {
        var (stat, qualifiers) = Stat(m.Groups["stat"].Value);
        if (stat is null) return null;
        int sign = m.Groups["verb"].Value.StartsWith("Increase", StringComparison.OrdinalIgnoreCase)
            || m.Groups["verb"].Value.StartsWith("Raise", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
        int dir = BetterWhenHigher(stat) switch { true => sign, false => -sign, null => 0 };
        var detail = qualifiers.Count == 0 ? "" : $" ({string.Join(", ", qualifiers)})";
        return ($"{stat}{detail} {(sign > 0 ? "+" : "-")}{m.Groups["n"].Value}{m.Groups["pc"].Value}", dir);
    }

    // "a range of 61 Fixed/44~33 Variable" → "61/44-33" (fixed/variable, whichever order the post has them in);
    // "25 Fixed" → "25 fixed"
    static string Value(string v)
    {
        v = v.Replace("a range of ", "").Replace('~', '-');
        if (VariableFirst().Match(v) is { Success: true } m) return $"{m.Groups["f"].Value}/{m.Groups["v"].Value}";
        return v.Replace(" Fixed/", "/").Replace(" Variable", "").Replace(" Fixed", " fixed");
    }

    static List<double> Numbers(string v) =>
        Number().Matches(v).Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToList();

    // the post's wording for a part of a move, and our short words for it
    static readonly (Regex Pattern, string? Words)[] Parts =
    [
        (Tail(@"on miss"), "on miss"), (Tail(@"on hit"), "on hit"),
        (Tail(@"after (?:the )?(?:charge|charging)"), "after charge"),
        (Tail(@"after (?:release|releasing [\w ]+?)"), "after release"),
        (Tail(@"of the (?:ground|grounded) and (?:aerial|air) versions"), "ground, air"),
        (Tail(@"(?:for|of|during) (?:the )?(?:aerial|air) version(?: of the attack)?"), "air"),
        (Tail(@"(?:for|of|during) (?:the )?(?:ground|grounded) version(?: of the attack)?"), "ground"),
        (Tail(@"of the far grounded version"), "far, ground"), (Tail(@"of the close grounded version"), "close, ground"),
        (Tail(@"of the charged (?:version|variant)"), "charged"), (Tail(@"of the uncharged (?:version|variant)"), "uncharged"),
        (Tail(@"of the wall (?:version|variant)"), "wall"), (Tail(@"of the normal (?:version|variant)"), "normal"),
        (Tail(@"of the (?:backwards?|back) Active Input(?: version)?|of the Active Input backwards? version"), "back input"),
        (Tail(@"of the forward Active Input(?: version)?|of the Active Input forward version"), "forward input"),
        (Tail(@"of the (?:upward|up) Active Input(?: version)?|of the Active Input (?:upward|up) version"), "up input"),
        (Tail(@"of the Active Input(?: version)?"), "active input"),
        (Tail(@"of all versions"), null),
        (Tail(@"at the (?:beginning|start) of the attack"), "start"), (Tail(@"at the end of the attack"), "end"),
        (Tail(@"of the initial (?:hit|swing|attack|strike)"), "first hit"),
        (Tail(@"of the (?<w>first|second|third|final|last) (?:hit|swing|slash|shot)"), "${w} hit"),
        (Tail(@"of the (?<w>earlier|later|early|late|first|second|final) Hit Window"), "${w} hit"),
        (Tail(@"at the end of the Hit Window"), "later hit"),
        (Tail(@"on the main target"), "main target"), (Tail(@"(?:of the [\w ]+? )?on secondary targets"), "other targets"),
        (Tail(@"on release"), "after release"), (Tail(@"on landing"), "landing"),
        (Tail(@"of (?:the )?attack"), null),
        // any other part, as patch notes name it: "of the setup hit", "of final hit", "of the air landing" (three words at
        // most: a part's name, never a sentence)
        (Tail(@"(?:of|on|for|during) (?:the |each |every |all )?(?<w>(?:[a-z-]+ ){0,3}(?:hits?|versions?|swings?|slash|landings?|strikes?|shots?|explosions?|projectiles?|blasts?|throws?|kicks?|punch|spin|dash|lunge|slam))"), "${w}"),
    ];

    static Regex Tail(string part) => new($@"\s+(?:{part})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The stat a phrase names, in our short words, and the part of the move it's about; null: a stat we don't
    /// name.</summary>
    static (string? Stat, List<string> Parts) Stat(string phrase)
    {
        var parts = new List<string>();
        var p = Leading().Replace(phrase, "");
        if (Grounded().Match(p) is { Success: true } lead)
        {
            parts.Add(lead.Groups[1].Value.ToLowerInvariant() is "aerial" or "air" ? "air" : "ground");
            p = p[lead.Length..];
        }
        // older notes: "Variable Force", "Fixed Recover Time" (force's two halves; a recovery's fixed and variable frames)
        if (FixedOrVariable().Match(p) is { Success: true } half)
        {
            parts.Add(half.Groups[1].Value.ToLowerInvariant());
            p = p[half.Length..];
        }
        int leading = parts.Count;   // the words before the stat first, then the parts after it in the post's order
        for (bool found = true; found;)
        {
            found = false;
            foreach (var (pattern, words) in Parts)
            {
                var q = pattern.Match(p);
                if (!q.Success) continue;
                if (words is not null)
                    parts.Insert(leading, q.Result(words).ToLowerInvariant()
                        .Replace("last hit", "final hit").Replace("early hit", "earlier hit").Replace("late hit", "later hit"));
                p = p[..q.Index];
                found = true;
                break;
            }
        }
        bool max = MaxDamage().IsMatch(phrase);
        bool min = MinCharge().IsMatch(phrase);
        var stat = p.ToLowerInvariant() switch
        {
            "damage" => max ? "max damage" : "damage",
            "force" => "force",
            "stun" => "stun",
            // only a minimum charge is certain (less is better); a shorter maximum can be a nerf (Wu Shang's Down Gauntlets had
            // its cut so the signature is easier to counter), and a bare "charge time" could be either
            "charge time" or "charge" => min ? "min charge" : max ? "max charge" : "charge time",
            "recover time" or "recovery time" or "recover" or "recovery" => "recovery",
            "cooldown" => "cooldown",
            "startup" or "startup time" => "startup",
            "hit window" => "hit window",
            "time to hit" => "time to hit",
            var s when CancelWindow().Match(s) is { Success: true } cw => $"{cw.Groups["k"].Value} cancel",
            _ => null,
        };
        return (stat, parts);
    }

    /// <summary>Certain directions only: more damage is better; more charge, startup, recovery, cooldown or time to hit,
    /// or a later cancel, is worse. Force, stun and hit windows: not certain.</summary>
    static bool? BetterWhenHigher(string stat) => stat switch
    {
        "damage" or "max damage" => true,
        "min charge" or "startup" or "recovery" or "cooldown" or "time to hit" => false,
        _ when stat.EndsWith(" cancel", StringComparison.Ordinal) => false,
        _ => null,
    };

    const string N = @"\d+(?:\.\d+)?(?:~\d+(?:\.\d+)?)?";
    // a value: "12", "61 Fixed/44~33 Variable", the older "10 Variable/35 Fixed" and "25 Fixed"
    const string V = $@"(?:a range of )?{N}(?: Fixed/{N} Variable| Variable/{N} Fixed| Fixed| Variable)?";

    [GeneratedRegex($@"^(?:Slightly |Significantly |Greatly )?(?:Increased?|Decreased?|Reduced?|Raised|Lowered|Changed|Adjusted|Delayed) (?<stat>.+?) from (?<a>{V}) to (?<b>{V})(?: to (?:match|keep|bring|make|align) .*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FactPattern();

    // older notes: "Cooldown on hit decreased from 16 to 11"
    [GeneratedRegex($@"^(?<stat>[A-Z][\w /-]+?) (?:was |is |has been )?(?:increased|decreased|reduced|raised|lowered|changed|adjusted) from (?<a>{V}) to (?<b>{V})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InvertedPattern();

    // "Decreased Variable Force by 32%", "Increased Damage by 2"
    [GeneratedRegex(@"^(?:Slightly |Significantly |Greatly )?(?<verb>Increased?|Decreased?|Reduced?|Raised|Lowered) (?<stat>.+?) by (?<n>\d+(?:\.\d+)?)(?<pc>%?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PercentPattern();

    [GeneratedRegex(@"^(fixed|variable) ", RegexOptions.IgnoreCase)]
    private static partial Regex FixedOrVariable();

    [GeneratedRegex(@"^(?<v>[\d.-]+) Variable/(?<f>[\d.-]+) Fixed$")]
    private static partial Regex VariableFirst();

    [GeneratedRegex(@"^(?:the |total |maximum total |maximum |minimum )+", RegexOptions.IgnoreCase)]
    private static partial Regex Leading();

    [GeneratedRegex(@"^(?:the )?minimum", RegexOptions.IgnoreCase)]
    private static partial Regex MinCharge();

    [GeneratedRegex(@"^(grounded|aerial|ground|air) ", RegexOptions.IgnoreCase)]
    private static partial Regex Grounded();

    [GeneratedRegex(@"^(?:the )?maximum", RegexOptions.IgnoreCase)]
    private static partial Regex MaxDamage();

    [GeneratedRegex(@"^(?<k>dash/dodge/jump|dash|dodge|jump|walk|attack) cancel window$")]
    private static partial Regex CancelWindow();

    [GeneratedRegex(@"\d+(?:\.\d+)?")]
    private static partial Regex Number();

    // a move: "Side Light", "Neutral Blasters", "Down Air", "Recovery", "Ground Pound", "Fused Side Air", "Aerial Attacks"
    // ("Down Light Finisher (from Side Light Bridge)": a part in brackets after it)
    [GeneratedRegex(@"^(?:(?:Fused |Split |Exhausted )?(?:Neutral|Side|Down|Up)\b[\w '&]*|(?:Fused |Split |Exhausted )?(?:Recovery|Ground Pound)|[\w ]*(?:Light|Air|Attacks?|Opener|Finishers?|Throw))(?: \([\w '&]+\))?$")]
    private static partial Regex Move();

    // between changes: "; ", ", and ", " and Increased...", and a sentence's end inside one bullet (older notes)
    [GeneratedRegex(@";|\.\s+(?=[A-Z])|, and (?=[a-z])|,? and (?=(?:increased|decreased|reduced|focused|lowered|raised|delayed|changed|adjusted|slightly|significantly)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex ClauseSplit();

    // words a balance change uses, for a line whose move can't be told
    [GeneratedRegex(@"\b(?:Increased|Decreased|Reduced|Lowered|Raised|Adjusted|Changed|Improved|Delayed|Damage|Force|Stun|Hurtbox|Hitbox|threat coverage|Recover|Hit Window|Time to Hit|momentum|acceleration|knockback)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Balance();
}
