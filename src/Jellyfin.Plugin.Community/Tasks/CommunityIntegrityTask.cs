using Jellyfin.Plugin.Community.Infrastructure;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Community.Tasks;

public sealed class CommunityIntegrityTask : IScheduledTask
{
    private readonly CommunityDatabase _database;
    private readonly ILogger<CommunityIntegrityTask> _logger;

    public CommunityIntegrityTask(CommunityDatabase database, ILogger<CommunityIntegrityTask> logger)
    {
        _database = database;
        _logger = logger;
    }

    public string Name => "Community integrity check";

    public string Key => "JellyfinCommunityIntegrity";

    public string Description => "Checks the integrity of the Community SQLite database.";

    public string Category => "Community";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(10);
        var result = await _database.IntegrityCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Community SQLite integrity check returned: {Result}", result);
        }
        else
        {
            _logger.LogInformation("Community SQLite integrity check completed successfully.");
        }

        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerInterval, IntervalTicks = TimeSpan.FromDays(7).Ticks }];
}
