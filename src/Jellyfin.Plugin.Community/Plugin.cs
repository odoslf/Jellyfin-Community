using System.Globalization;
using Jellyfin.Plugin.Community.Configuration;
using Jellyfin.Plugin.Community.WebIntegration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Community;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static readonly Guid PluginId = Guid.Parse("C24C5B8E-2FA8-47F6-A671-A7EB9D60C114");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Community";

    public override string Description => "Foro comunitario local integrado con las cuentas y bibliotecas de Jellyfin.";

    public override Guid Id => PluginId;

    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;
        return
        [
            new PluginPageInfo
            {
                Name = "Community",
                DisplayName = "Comunidad",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.community.html", ns),
                EnableInMainMenu = false
            },
            new PluginPageInfo
            {
                Name = "CommunityPageController",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.communityPageController.js", ns),
                EnableInMainMenu = false
            },
            new PluginPageInfo
            {
                Name = CommunityIndexHtmlTransformer.BootstrapPageName,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.communityBootstrap.js", ns),
                EnableInMainMenu = false
            },
            new PluginPageInfo
            {
                Name = "CommunityConfiguration",
                DisplayName = "Community",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", ns),
                EnableInMainMenu = false
            }
        ];
    }
}
