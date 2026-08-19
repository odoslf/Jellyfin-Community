using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Community;

public sealed class CommunityChannel : IChannel
{
    public string Name => "Foro";

    public string Description => "Foro comunitario local para debatir y conversar.";

    public string DataVersion => typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "1.6.0.0";

    public string HomePageUrl => "../Community/app";

    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public InternalChannelFeatures GetChannelFeatures()
    {
        return new InternalChannelFeatures
        {
            ContentTypes = [ChannelMediaContentType.Podcast],
            MediaTypes = [ChannelMediaType.Video],
            MaxPageSize = 50,
            SupportsSortOrderToggle = false
        };
    }

    public bool IsEnabledFor(string userId)
    {
        return Plugin.Instance?.Configuration.Enabled ?? true;
    }

    public Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var items = new List<ChannelItemInfo>
        {
            new ChannelItemInfo
            {
                Id = "community_forum_access",
                Name = "Acceder al Foro Comunitario",
                Overview = "Haga clic aquí para abrir el Foro Comunitario de Jellyfin.",
                Type = ChannelItemType.Folder,
                FolderType = ChannelFolderType.Container,
                HomePageUrl = "../Community/app"
            }
        };

        return Task.FromResult(new ChannelItemResult
        {
            Items = items,
            TotalRecordCount = items.Count
        });
    }

    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
    {
        return Task.FromResult(new DynamicImageResponse());
    }

    public IEnumerable<ImageType> GetSupportedChannelImages()
    {
        return [ImageType.Primary];
    }
}
