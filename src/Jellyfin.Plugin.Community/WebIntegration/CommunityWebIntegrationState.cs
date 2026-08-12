namespace Jellyfin.Plugin.Community.WebIntegration;

public sealed class CommunityWebIntegrationState
{
    private long _indexRequestsSeen;
    private long _indexResponsesTransformed;
    private long _configRequestsSeen;
    private long _configResponsesTransformed;
    private long _lastInjectionUnixMilliseconds;
    private string? _lastError;

    public long IndexRequestsSeen => Interlocked.Read(ref _indexRequestsSeen);

    public long IndexResponsesTransformed => Interlocked.Read(ref _indexResponsesTransformed);

    public long ConfigRequestsSeen => Interlocked.Read(ref _configRequestsSeen);

    public long ConfigResponsesTransformed => Interlocked.Read(ref _configResponsesTransformed);

    public DateTime? LastInjectionUtc
    {
        get
        {
            var milliseconds = Interlocked.Read(ref _lastInjectionUnixMilliseconds);
            return milliseconds <= 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
        }
    }

    public string? LastError => Volatile.Read(ref _lastError);

    public void RecordIndexRequest() => Interlocked.Increment(ref _indexRequestsSeen);

    public void RecordConfigRequest() => Interlocked.Increment(ref _configRequestsSeen);

    public void RecordIndexTransformed()
    {
        Interlocked.Increment(ref _indexResponsesTransformed);
        RecordSuccess();
    }

    public void RecordConfigTransformed()
    {
        Interlocked.Increment(ref _configResponsesTransformed);
        RecordSuccess();
    }

    private void RecordSuccess()
    {
        Interlocked.Exchange(ref _lastInjectionUnixMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Volatile.Write(ref _lastError, null);
    }

    public void RecordError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Volatile.Write(ref _lastError, exception.GetType().Name + ": " + exception.Message);
    }
}
