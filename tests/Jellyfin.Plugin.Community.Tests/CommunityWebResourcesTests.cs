namespace Jellyfin.Plugin.Community.Tests;

public sealed class CommunityWebResourcesTests
{
    [Theory]
    [InlineData("Jellyfin.Plugin.Community.Web.communityForum15.html")]
    [InlineData("Jellyfin.Plugin.Community.Web.communityForum15.css")]
    [InlineData("Jellyfin.Plugin.Community.Web.communityForum15.js")]
    [InlineData("Jellyfin.Plugin.Community.Web.communityBootstrap.js")]
    [InlineData("Jellyfin.Plugin.Community.Configuration.configPage.html")]
    public void RequiredWebResourceIsEmbedded(string resourceName)
    {
        using var resource = typeof(Plugin).Assembly.GetManifestResourceStream(resourceName);

        Assert.NotNull(resource);
        Assert.True(resource.Length > 0);
    }
}
