namespace Jellyfin.Plugin.Community.WebIntegration;

public sealed class CommunityWebIntegrationState
{
    private long _indexRequestsSeen;
    private long _indexResponsesTransformed;
    private long _lastInjectionUnixMilliseconds;
    private string? _lastError;

    public long IndexRequestsSeen => Interlocked.Read(ref _indexRequestsSeen);

    public long IndexResponsesTransformed => Interlocked.Read(ref _indexResponsesTransformed);

    public DateTime? LastInjectionUtc
    {
        get
        {
            var milliseconds = Interlocked.Read(ref _lastInjectionUnixMilliseconds);
            return milliseconds <= 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
        }
    }

    public string? LastError => Volatile.Read(ref _lastError);

    public void RecordRequest() => Interlocked.Increment(ref _indexRequestsSeen);

    public void RecordTransformed()
    {
        Interlocked.Increment(ref _indexResponsesTransformed);
        Interlocked.Exchange(ref _lastInjectionUnixMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Volatile.Write(ref _lastError, null);
    }

    public void RecordError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Volatile.Write(ref _lastError, exception.GetType().Name + ": " + exception.Message);
    }
}
