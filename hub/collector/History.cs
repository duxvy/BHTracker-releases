using System.Security.Cryptography;
using System.Text;

namespace BHTracker.Hub;

/// <summary>
/// The Hub's history on hub-data: every official post since 2014 and every balance post's changes since
/// <see cref="PatchesSince"/>, one file a year (history/2026.json). Made whole from Steam once (a page of 100 at a time,
/// about ten answers) and whenever <see cref="Version"/> goes up; after that each run puts the newest posts in, and a
/// year's file is written only when its content changes - so in practice only when a new post arrives.
/// </summary>
public static class History
{
    /// <summary>The rules that make the files: raise it when the summaries, categories or patch facts change, and the
    /// next run makes every year again from Steam.</summary>
    public const int Version = 1;

    /// <summary>Balance changes from 2016 on: every year since is 67-88% clean facts; 2015's notes are 19%, 2014's
    /// (the beta) none (docs/decisions.md, "The Hub's history").</summary>
    public const int PatchesSince = 2016;

    public const string Folder = "history";

    public static HubNews News(Post post, Names names) =>
        new(post.Id, post.Title, post.Date, Summaries.For(post.Title, post.Contents, names), post.Url,
            Categories.For(post.Title, post.Contents, names));

    /// <summary>A post's balance changes, or null: not a balance post, before <see cref="PatchesSince"/>, or nothing read.</summary>
    public static HubPatch? Patch(Post post, Names names)
    {
        if (post.Date.UtcDateTime.Year < PatchesSince || !Summaries.IsBalancePost(post.Title, post.Contents, names)) return null;
        var subjects = PatchNotes.Read(post.Contents, names);
        return subjects.Count == 0 ? null : new HubPatch(Summaries.PatchLabel(post.Title), post.Title, post.Date, post.Url, subjects);
    }

    /// <summary>Every year, from all the posts.</summary>
    public static IReadOnlyDictionary<int, HistoryYear> Build(IEnumerable<Post> posts, Names names) =>
        Merge(new Dictionary<int, HistoryYear>(), posts, names);

    /// <summary>The years with these posts put in: one already there is made again from its text now, one with the same
    /// headline the same day counts as the same post.</summary>
    public static IReadOnlyDictionary<int, HistoryYear> Merge(IReadOnlyDictionary<int, HistoryYear> years, IEnumerable<Post> posts, Names names)
    {
        var fresh = SteamNews.Dedupe(posts);
        var result = years.ToDictionary(y => y.Key, y => y.Value);
        foreach (var year in fresh.GroupBy(p => p.Date.UtcDateTime.Year))
        {
            var news = year.Select(p => News(p, names)).ToList();
            var patches = year.Select(p => Patch(p, names)).OfType<HubPatch>().ToList();
            var ids = year.Select(p => p.Id).ToHashSet();
            var same = year.Select(p => (p.Title, DateOnly.FromDateTime(p.Date.UtcDateTime))).ToHashSet();
            var kept = result.GetValueOrDefault(year.Key);
            var oldNews = (kept?.News ?? []).Where(n => !ids.Contains(n.Id) && !same.Contains((n.Title, DateOnly.FromDateTime(n.Date.UtcDateTime))));
            var oldPatches = (kept?.Patches ?? []).Where(p => !ids.Contains(Id(p.Url)) && !same.Contains((p.Title, DateOnly.FromDateTime(p.Date.UtcDateTime))));
            result[year.Key] = new HistoryYear(Version, year.Key,
                news.Concat(oldNews).OrderByDescending(n => n.Date).ThenBy(n => n.Id, StringComparer.Ordinal).ToList(),
                patches.Concat(oldPatches).OrderByDescending(p => p.Date).ThenBy(p => p.Url, StringComparer.Ordinal).ToList());
        }
        return result;
    }

    static string Id(string url) => url[(url.LastIndexOf('/') + 1)..];

    /// <summary>The years the index lists, read from the folder; null when one is missing, unreadable, or made with other
    /// rules (then everything is made again).</summary>
    public static IReadOnlyDictionary<int, HistoryYear>? Read(string outDir, HistoryIndex? index)
    {
        if (index is null || index.Version != Version || index.PatchesSince != PatchesSince) return null;
        var years = new Dictionary<int, HistoryYear>();
        foreach (var file in index.Files)
        {
            var path = Path.Combine(outDir, file.File.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) || HistoryYear.FromJson(File.ReadAllText(path)) is not { } year || year.Version != Version) return null;
            years[file.Year] = year;
        }
        return years;
    }

    /// <summary>Writes each year whose text changed, newest first; the index of them all, and how many were written.</summary>
    public static (HistoryIndex Index, int Written) Write(string outDir, IReadOnlyDictionary<int, HistoryYear> years)
    {
        int written = 0;
        var files = new List<HistoryFile>();
        foreach (var (y, year) in years.OrderByDescending(y => y.Key))
        {
            var name = $"{Folder}/{y}.json";
            var path = Path.Combine(outDir, Folder, $"{y}.json");
            var text = year.ToJson();
            if (!File.Exists(path) || File.ReadAllText(path) != text)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path + ".tmp", text);
                File.Move(path + ".tmp", path, overwrite: true);
                written++;
            }
            files.Add(new HistoryFile(y, name, year.News.Count, year.Patches.Count, Sha(text)));
        }
        return (new HistoryIndex(Version, PatchesSince, files), written);
    }

    /// <summary>A file's text, hashed: the first 16 hex digits of its SHA-256 (what the app compares).</summary>
    public static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
}
