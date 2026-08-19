using Jellyfin.Plugin.Community.Infrastructure;
using Jellyfin.Plugin.Community.Services;
using Jellyfin.Plugin.Community.WebIntegration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
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
        serviceCollection.AddSingleton<CommunityWebIntegrationState>();
        serviceCollection.AddSingleton<MediaBrowser.Controller.Channels.IChannel, CommunityChannel>();
        serviceCollection.AddTransient<IStartupFilter, CommunityStartupFilter>();
        serviceCollection.AddHostedService<CommunityStartupService>();
    }
}
