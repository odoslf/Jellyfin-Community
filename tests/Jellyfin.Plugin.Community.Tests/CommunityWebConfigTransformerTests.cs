using System.Text.Json.Nodes;
using Jellyfin.Plugin.Community.WebIntegration;

namespace Jellyfin.Plugin.Community.Tests;

public sealed class CommunityWebConfigTransformerTests
{
    [Fact]
    public void AddForumMenuLinkPreservesExistingLinksAndUsesRelativeServerUrl()
    {
        const string source = """
            {
              "theme": "dark",
              "menuLinks": [
                { "name": "Ayuda", "icon": "help", "url": "https://example.test/help" }
              ]
            }
            """;

        var transformed = CommunityWebConfigTransformer.AddForumMenuLink(source, new Version(1, 5, 0, 0));
        var root = JsonNode.Parse(transformed)!.AsObject();
        var links = root["menuLinks"]!.AsArray();

        Assert.Equal("dark", root["theme"]!.GetValue<string>());
        Assert.Equal(2, links.Count);
        Assert.Equal("Foro", links[0]!["name"]!.GetValue<string>());
        Assert.Equal("forum", links[0]!["icon"]!.GetValue<string>());
        Assert.Equal("../Community/app?v=1.5.0.0", links[0]!["url"]!.GetValue<string>());
        Assert.Equal("Ayuda", links[1]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void AddForumMenuLinkReplacesPreviousCommunityEntry()
    {
        const string source = """
            {
              "menuLinks": [
                { "name": "Foro", "url": "/old/Community/app?v=1.4.0.0" },
                { "name": "Otro", "url": "/other" }
              ]
            }
            """;

        var transformed = CommunityWebConfigTransformer.AddForumMenuLink(source, new Version(1, 5, 0, 0));
        var links = JsonNode.Parse(transformed)!["menuLinks"]!.AsArray();

        Assert.Equal(2, links.Count);
        Assert.Single(links.Where(link => link?["name"]?.GetValue<string>() == "Foro"));
        Assert.Equal("../Community/app?v=1.5.0.0", links[0]!["url"]!.GetValue<string>());
    }

    [Fact]
    public void AddForumMenuLinkCreatesMissingArray()
    {
        var transformed = CommunityWebConfigTransformer.AddForumMenuLink("{\"useProxy\":true}", new Version(1, 5, 0, 0));
        var root = JsonNode.Parse(transformed)!.AsObject();

        Assert.Single(root["menuLinks"]!.AsArray());
        Assert.True(root["useProxy"]!.GetValue<bool>());
    }
}
