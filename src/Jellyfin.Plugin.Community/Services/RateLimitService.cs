using System.Collections.Concurrent;
using Jellyfin.Plugin.Community.Domain;

namespace Jellyfin.Plugin.Community.Services;

public sealed class RateLimitService
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _events = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, DateTime> _lastPost = new();

    public void CheckThreadCreation(CommunityUserContext user)
    {
        Check(user, "thread", Math.Max(1, Plugin.Instance?.Configuration.NewThreadsPerHour ?? 5), TimeSpan.FromHours(1));
        CheckMinimumGap(user);
    }

    public void CheckReply(CommunityUserContext user)
    {
        Check(user, "reply", Math.Max(1, Plugin.Instance?.Configuration.RepliesPerHour ?? 30), TimeSpan.FromHours(1));
        CheckMinimumGap(user);
    }

    private void Check(CommunityUserContext user, string action, int limit, TimeSpan period)
    {
        if (user.IsModerator)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var key = user.UserId.ToString("D") + ":" + action;
        var queue = _events.GetOrAdd(key, static _ => new Queue<DateTime>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > period)
            {
                queue.Dequeue();
            }

            if (queue.Count >= limit)
            {
                throw new CommunityRateLimitException("The Community rate limit has been reached. Try again later.");
            }

            queue.Enqueue(now);
        }
    }

    private void CheckMinimumGap(CommunityUserContext user)
    {
        if (user.IsModerator)
        {
            return;
        }

        var minimum = TimeSpan.FromSeconds(Math.Max(0, Plugin.Instance?.Configuration.MinimumSecondsBetweenPosts ?? 10));
        var now = DateTime.UtcNow;
        if (_lastPost.TryGetValue(user.UserId, out var previous) && now - previous < minimum)
        {
            throw new CommunityRateLimitException($"Wait at least {minimum.TotalSeconds:0} seconds between posts.");
        }

        _lastPost[user.UserId] = now;
    }
}

public sealed class CommunityRateLimitException : Exception
{
    public CommunityRateLimitException(string message)
        : base(message)
    {
    }
}
