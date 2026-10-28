using System.Net;
using System.Text.RegularExpressions;

namespace BHTracker.Hub;

/// <summary>A post cut at a heading: the heading's plain text ("" for a picture-only heading, or before the first) and
/// the bullets under it, each as plain text.</summary>
public sealed record Section(string Heading, IReadOnlyList<string> Bullets);

/// <summary>Reading Steam's BBCode: plain text, and the post's headings with the bullets under each.</summary>
public static partial class Bbcode
{
    /// <summary>Text without tags or entities, spaces collapsed, curly quotes made straight.</summary>
    public static string Plain(string s)
    {
        s = WebUtility.HtmlDecode(Tag().Replace(s, " "));
        s = s.Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"').Replace(' ', ' ');
        return Space().Replace(s, " ").Trim();
    }

    /// <summary>The post's sections in order. A heading may hold a picture and a line break before its words
    /// ("[h2][img][/img]\nGreatsword[/h2]"), so headings are matched across lines.</summary>
    public static IReadOnlyList<Section> Sections(string bbcode)
    {
        var sections = new List<Section>();
        string heading = "";
        int start = 0;
        foreach (Match h in Heading().Matches(bbcode))
        {
            sections.Add(new(heading, Bullets(bbcode[start..h.Index])));
            heading = Plain(h.Groups[2].Value);
            start = h.Index + h.Length;
        }
        sections.Add(new(heading, Bullets(bbcode[start..])));
        return sections;
    }

    static List<string> Bullets(string body) =>
        Bullet().Matches(body).Select(m => Plain(m.Groups[1].Value)).Where(b => b.Length > 0).ToList();

    [GeneratedRegex(@"\[h([1-6])\](.*?)\[/h\1\]", RegexOptions.Singleline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"\[\*\](.*?)(?=\[\*\]|\[/list\]|$)", RegexOptions.Singleline)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
}
