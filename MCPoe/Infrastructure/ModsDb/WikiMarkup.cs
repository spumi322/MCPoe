using System.Net;
using System.Text.RegularExpressions;

namespace MCPoe.Infrastructure.ModsDb;

internal static partial class WikiMarkup
{
    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        // Decode entities first so encoded tags like &lt;br&gt; become real tags we can strip.
        var s = WebUtility.HtmlDecode(raw);
        // [[Target|Display]] -> Display, [[Target]] -> Target
        s = WikiLinkPiped().Replace(s, "$2");
        s = WikiLink().Replace(s, "$1");
        // <br>, <br/>, <br /> -> newline
        s = BrTag().Replace(s, "\n");
        // strip remaining HTML tags
        s = HtmlTag().Replace(s, string.Empty);
        // collapse repeated whitespace inside lines, preserve newlines
        s = InlineWs().Replace(s, " ");
        return s.Trim();
    }

    [GeneratedRegex(@"\[\[([^\]|]+)\|([^\]]+)\]\]")]
    private static partial Regex WikiLinkPiped();

    [GeneratedRegex(@"\[\[([^\]]+)\]\]")]
    private static partial Regex WikiLink();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex InlineWs();
}
