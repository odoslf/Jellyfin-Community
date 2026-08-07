using Jellyfin.Plugin.Community.Services;

namespace Jellyfin.Plugin.Community.Tests;

public sealed class MarkdownServiceTests
{
    [Fact]
    public void RenderStripsRawHtmlAndDangerousLinks()
    {
        var service = new MarkdownService();
        var result = service.Render("<script>alert(1)</script> [js](javascript:alert(1)) [data](data:text/html;base64,PHNjcmlwdD4=) [vb](vbscript:msgbox(1)) **safe**");

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:text", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vbscript:", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<strong>safe</strong>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderDoesNotEmbedRemoteImagesByDefault()
    {
        var service = new MarkdownService();
        var result = service.Render("![private-alt](https://tracker.example/pixel.png)");

        Assert.DoesNotContain("<img", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tracker.example", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("private-alt", result, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeRemovesNullAndNormalizesNewlines()
    {
        var service = new MarkdownService();

        Assert.Equal("one\ntwo\nthree", service.Normalize("\0one\r\ntwo\rthree\0"));
    }

    [Fact]
    public void ExtractMentionsReturnsUniqueNames()
    {
        var service = new MarkdownService();
        var result = service.ExtractMentions("Hello @alice and @Bob, again @ALICE");

        Assert.Equal(2, result.Count);
        Assert.Contains(result, value => value.Equals("alice", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result, value => value.Equals("bob", StringComparison.OrdinalIgnoreCase));
    }
}
