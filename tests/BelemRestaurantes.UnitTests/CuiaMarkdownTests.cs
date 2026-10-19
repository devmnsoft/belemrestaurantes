using BelemRestaurantes.Application.Formatting;
using Xunit;

namespace BelemRestaurantes.UnitTests;

public sealed class CuiaMarkdownTests
{
    [Fact]
    public void ToHtml_ClosesHeadingsAndEncodesMarkup()
    {
        var html = CuiaMarkdown.ToHtml("## Título\n\nTexto com <script> e **negrito**.\n- [Pratos](/pratos)\n- item");

        Assert.Contains("<h2>T&#237;tulo</h2>", html);
        Assert.Contains("<strong>negrito</strong>", html);
        Assert.Contains("<a href=\"/pratos\">Pratos</a>", html);
        Assert.Contains("<ul><li>", html);
        Assert.Contains("</ul>", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Equal(1, Count(html, "<h2>"));
        Assert.Equal(1, Count(html, "</h2>"));
    }

    private static int Count(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
