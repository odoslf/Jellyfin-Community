using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;

namespace Jellyfin.Plugin.Community.Tests;

public sealed class CommunityChannelTests
{
    [Fact]
    public async Task Root_exposes_forum_entry()
    {
        var channel = new CommunityChannel();
        var result = await channel.GetChannelItems(new InternalChannelItemQuery(), CancellationToken.None);

        Assert.Equal("Foro", channel.Name);
        Assert.Equal("../Community/app?v=1.6.0.0", channel.HomePageUrl);
        Assert.True(channel.IsEnabledFor("test-user"));
        Assert.Single(result.Items);
        Assert.Equal("community_forum_access", result.Items[0].Id);
        Assert.Equal("Acceder al Foro Comunitario", result.Items[0].Name);
        Assert.Equal(ChannelItemType.Folder, result.Items[0].Type);
    }

    [Fact]
    public async Task Child_folder_is_empty_and_safe()
    {
        var channel = new CommunityChannel();
        var result = await channel.GetChannelItems(
            new InternalChannelItemQuery { FolderId = "community_forum_access" },
            CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalRecordCount);
    }
}
