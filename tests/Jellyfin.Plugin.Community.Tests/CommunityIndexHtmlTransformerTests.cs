using Jellyfin.Plugin.Community.WebIntegration;

namespace Jellyfin.Plugin.Community.Tests;

public sealed class CommunityIndexHtmlTransformerTests
{
    [Fact]
    public void InjectBootstrapAddsOneScriptBeforeBodyClose()
    {
        const string html = "<!doctype html><html><body><main>Jellyfin</main></body></html>";

        var transformed = CommunityIndexHtmlTransformer.InjectBootstrap(html, new Version(1, 1, 0, 0));

        Assert.Contains("data-jellyfin-community-bootstrap=\"1.1.0.0\"", transformed, StringComparison.Ordinal);
        Assert.Contains("./ConfigurationPage?name=CommunityBootstrap&amp;v=1.1.0.0", transformed, StringComparison.Ordinal);
        Assert.True(transformed.IndexOf(CommunityIndexHtmlTransformer.MarkerAttribute, StringComparison.Ordinal) < transformed.IndexOf("</body>", StringComparison.Ordinal));
    }

    [Fact]
    public void InjectBootstrapIsIdempotent()
    {
        const string html = "<html><body></body></html>";
        var first = CommunityIndexHtmlTransformer.InjectBootstrap(html, new Version(1, 1, 0, 0));

        var second = CommunityIndexHtmlTransformer.InjectBootstrap(first, new Version(1, 1, 0, 0));

        Assert.Equal(first, second);
        Assert.Equal(1, CountOccurrences(second, CommunityIndexHtmlTransformer.MarkerAttribute));
    }

    [Fact]
    public void InjectBootstrapLeavesMalformedDocumentUntouched()
    {
        const string html = "<html><main>missing body close</main></html>";

        var transformed = CommunityIndexHtmlTransformer.InjectBootstrap(html, new Version(1, 1, 0, 0));

        Assert.Equal(html, transformed);
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }

        return count;
    }
}
