using Jellyfin.Plugin.Community.Infrastructure;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Community.Tasks;

public sealed class CommunityOptimizeTask : IScheduledTask
{
    private readonly CommunityDatabase _database;

    public CommunityOptimizeTask(CommunityDatabase database)
    {
        _database = database;
    }

    public string Name => "Community database optimization";

    public string Key => "JellyfinCommunityOptimize";

    public string Description => "Runs SQLite optimization and statistics maintenance.";

    public string Category => "Community";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(10);
        await _database.OptimizeAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerInterval, IntervalTicks = TimeSpan.FromDays(7).Ticks }];
}
