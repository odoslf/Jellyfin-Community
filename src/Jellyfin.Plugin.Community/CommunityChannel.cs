using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Community;

public sealed class CommunityChannel : IChannel
{
    private const string ForumUrl = "../Community/app?v=1.6.0.0";

    public string Name => "Foro";

    public string Description => "Foro comunitario local para debatir y conversar.";

    public string DataVersion => typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "1.6.0.0";

    public string HomePageUrl => ForumUrl;

    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public InternalChannelFeatures GetChannelFeatures()
    {
        return new InternalChannelFeatures
        {
            ContentTypes = [ChannelMediaContentType.Podcast],
            MediaTypes = [ChannelMediaType.Video],
            MaxPageSize = 50,
            SupportsSortOrderToggle = false,
            SupportsContentDownloading = false
        };
    }

    public bool IsEnabledFor(string userId)
    {
        return Plugin.Instance?.Configuration.Enabled ?? true;
    }

    public Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrEmpty(query.FolderId))
        {
            return Task.FromResult(new ChannelItemResult
            {
                Items = [],
                TotalRecordCount = 0
            });
        }

        var items = new List<ChannelItemInfo>
        {
            new()
            {
                Id = "community_forum_access",
                Name = "Acceder al Foro Comunitario",
                Overview = "Abre la aplicación del Foro Comunitario de Jellyfin. En clientes nativos, el canal Foro sigue siendo visible aunque la interfaz completa de escritura se ofrece mediante Jellyfin Web/WebView.",
                Type = ChannelItemType.Folder,
                FolderType = ChannelFolderType.Container,
                HomePageUrl = ForumUrl
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
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DynamicImageResponse { HasImage = false });
    }

    public IEnumerable<ImageType> GetSupportedChannelImages()
    {
        return [];
    }
}
