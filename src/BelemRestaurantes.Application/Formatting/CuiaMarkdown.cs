using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BelemRestaurantes.Application.Formatting;

/// <summary>Markdown curto da Cuia IA. O texto é codificado antes de virar HTML.</summary>
public static partial class CuiaMarkdown
{
    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var builder = new StringBuilder();
        string? listKind = null;
        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                CloseList();
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                CloseList();
                builder.Append("<h3>").Append(Inline(line[4..])).Append("</h3>");
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                CloseList();
                builder.Append("<h2>").Append(Inline(line[3..])).Append("</h2>");
                continue;
            }

            if (BareOrdinalPattern().IsMatch(line)) { CloseList(); continue; }
            var ordered=OrderedItemPattern().Match(line);
            if (line.StartsWith("- ", StringComparison.Ordinal) || ordered.Success)
            {
                var tag=ordered.Success?"ol":"ul";
                if(listKind!=tag){CloseList();builder.Append('<').Append(tag).Append('>');listKind=tag;}
                builder.Append("<li>").Append(Inline(ordered.Success?ordered.Groups[1].Value:line[2..])).Append("</li>");
                continue;
            }

            CloseList();
            builder.Append("<p>").Append(Inline(line)).Append("</p>");
        }

        CloseList();
        return builder.ToString();

        void CloseList()
        {
            if (listKind is null) return;
            builder.Append("</").Append(listKind).Append('>');
            listKind = null;
        }
    }

    private static string Inline(string text)
    {
        var encoded = WebUtility.HtmlEncode(text);
        encoded = BoldPattern().Replace(encoded, "<strong>$1</strong>");
        encoded = ItalicPattern().Replace(encoded, "<em>$1</em>");
        return LinkPattern().Replace(encoded, match =>
        {
            var label = match.Groups[1].Value;
            var url = match.Groups[2].Value;
            if (url.StartsWith("//", StringComparison.Ordinal) || url.Contains('\\', StringComparison.Ordinal))
                return label;
            return $"<a href=\"{url}\">{label}</a>";
        });
    }

    [GeneratedRegex(@"^\d+\.\s+(.+)$")]
    private static partial Regex OrderedItemPattern();
    [GeneratedRegex(@"^\d+\.\s*$")]
    private static partial Regex BareOrdinalPattern();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldPattern();

    [GeneratedRegex(@"(?<!\*)\*(.+?)\*(?!\*)")]
    private static partial Regex ItalicPattern();

    [GeneratedRegex(@"\[(.+?)\]\((/(?!/)[^\s)]+|https://[^\s)]+)\)")]
    private static partial Regex LinkPattern();
}
