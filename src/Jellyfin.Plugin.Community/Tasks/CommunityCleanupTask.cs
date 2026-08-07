using Jellyfin.Plugin.Community.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Community.Tasks;

public sealed class CommunityCleanupTask : IScheduledTask
{
    private readonly CommunityService _community;

    public CommunityCleanupTask(CommunityService community)
    {
        _community = community;
    }

    public string Name => "Community cleanup";

    public string Key => "JellyfinCommunityCleanup";

    public string Description => "Purges expired notifications, drafts and deleted attachments.";

    public string Category => "Community";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(5);
        await _community.CleanupAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerDaily, TimeOfDayTicks = TimeSpan.FromHours(3).Ticks }];
}
