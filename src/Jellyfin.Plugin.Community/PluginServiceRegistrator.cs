using Jellyfin.Plugin.Community.Infrastructure;
using Jellyfin.Plugin.Community.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Community;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<CommunityPaths>();
        serviceCollection.AddSingleton<CommunityDatabase>();
        serviceCollection.AddSingleton<MarkdownService>();
        serviceCollection.AddSingleton<CurrentUserService>();
        serviceCollection.AddSingleton<PermissionService>();
        serviceCollection.AddSingleton<RateLimitService>();
        serviceCollection.AddSingleton<NotificationService>();
        serviceCollection.AddSingleton<AttachmentService>();
        serviceCollection.AddSingleton<BackupArchiveValidator>();
        serviceCollection.AddSingleton<BackupService>();
        serviceCollection.AddSingleton<CommunityService>();
        serviceCollection.AddHostedService<CommunityStartupService>();
    }
}
