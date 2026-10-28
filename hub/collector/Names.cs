using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BHTracker.Hub;

/// <summary>The names the collector knows: legends and their weapons, the weapons, crossover skins (BHTracker's own
/// bundled lists) and Brawlhalla's recurring events (the list below). Summaries name only what's on these lists.</summary>
public sealed class Names
{
    /// <summary>Brawlhalla's recurring events, as players know them.</summary>
    public static readonly string[] Events =
    [
        "Back to School", "Heatwave", "Bloomhalla", "Brawlhalla Fest", "Brawlhalloween", "Brawlhallidays", "Valhallentine",
        "Lunar New Year", "May the 4th", "Brawlhalla Star Wars Event", "Brawlhalla Expo",
    ];

    // the API's weapon keys, as patch notes and players name them
    static readonly Dictionary<string, string> WeaponNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Fists"] = "Gauntlets", ["Katar"] = "Katars", ["Pistol"] = "Blasters", ["RocketLance"] = "Rocket Lance",
        ["Boots"] = "Battle Boots",
    };

    readonly Dictionary<string, string> _subjects = new();   // folded name -> its name here
    readonly List<string> _byLength;                         // folded names, longest first

    public IReadOnlyDictionary<string, (string One, string Two)> Legends { get; }
    public IReadOnlyList<string> Weapons { get; }
    public IReadOnlyList<string> Crossovers { get; }

    public Names(IEnumerable<(string Name, string One, string Two)> legends, IEnumerable<string> crossovers)
    {
        var map = new Dictionary<string, (string, string)>();
        foreach (var (name, one, two) in legends) map[name] = (Weapon(one), Weapon(two));
        Legends = map;
        Weapons = map.Values.SelectMany(w => new[] { w.Item1, w.Item2 }).Where(w => w.Length > 0).Distinct().Order().ToList();
        Crossovers = crossovers.Where(c => c.Length > 2).Distinct().OrderByDescending(c => c.Length).ToList();
        foreach (var s in Legends.Keys.Concat(Weapons)) _subjects[Fold(s)] = s;
        _byLength = _subjects.Keys.OrderByDescending(k => k.Length).ToList();
    }

    static Names? _bundled;

    /// <summary>The lists embedded in the collector.</summary>
    public static Names Bundled => _bundled ??= Load();

    static Names Load()
    {
        using var legends = JsonDocument.Parse(Resource("legends.json"));
        using var crossovers = JsonDocument.Parse(Resource("crossovers.json"));
        var details = legends.RootElement.GetProperty("details").EnumerateArray()
            .Select(d => (d.GetProperty("name").GetString() ?? "", d.GetProperty("weaponOne").GetString() ?? "",
                d.GetProperty("weaponTwo").GetString() ?? ""))
            .Where(d => d.Item1.Length > 0);
        var skins = crossovers.RootElement.GetProperty("crossovers").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString() ?? "");
        return new Names(details, skins);
    }

    static string Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"{name} isn't embedded");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>"legend", "weapon", or null for a name that's neither.</summary>
    public string? KindOf(string subject) =>
        Legends.ContainsKey(subject) ? "legend" : Weapons.Contains(subject) ? "weapon" : null;

    /// <summary>The legend or weapon a heading names (accents and case aside), or null.</summary>
    public string? Subject(string heading) => _subjects.GetValueOrDefault(Fold(heading.Trim()));

    /// <summary>The legend or weapon a text starts with, followed by a space, and the rest: "Rocket Lance Neutral Air" →
    /// (Rocket Lance, Neutral Air). The longest name wins.</summary>
    public (string Subject, string Move)? StartsWith(string text)
    {
        var folded = Fold(text);
        foreach (var key in _byLength)
            if (folded.Length > key.Length + 1 && folded.StartsWith(key, StringComparison.Ordinal) && folded[key.Length] == ' ')
                return (_subjects[key], text[(key.Length + 1)..].Trim());
        return null;
    }

    /// <summary>A legend named in a text at a word boundary, the longest first.</summary>
    public string? LegendIn(string text) =>
        Legends.Keys.OrderByDescending(k => k.Length).FirstOrDefault(k => Contains(Fold(text), Fold(k)));

    /// <summary>A crossover skin named in a text at a word boundary, the longest first.</summary>
    public string? CrossoverIn(string text) => Crossovers.FirstOrDefault(c => Contains(Fold(text), Fold(c)));

    static bool Contains(string text, string name) =>
        Regex.IsMatch(text, $@"(?<![\w]){Regex.Escape(name)}(?![\w])");

    static string Weapon(string key) => WeaponNames.GetValueOrDefault(key, key);

    /// <summary>Lower case without accents, one letter for one letter ("Bödvar" → "bodvar"), so lengths line up.</summary>
    public static string Fold(string s)
    {
        var chars = new char[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsSurrogate(s[i])) { chars[i] = s[i]; continue; }   // half of an emoji: as it is
            var d = s[i].ToString().Normalize(NormalizationForm.FormD);
            chars[i] = char.ToLowerInvariant(d.Length > 0 && CharUnicodeInfo.GetUnicodeCategory(d[0]) != UnicodeCategory.NonSpacingMark ? d[0] : s[i]);
        }
        return new string(chars);
    }
}
