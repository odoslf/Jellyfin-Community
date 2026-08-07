using Jellyfin.Plugin.Community.Domain;
using Jellyfin.Plugin.Community.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Community.Services;

public sealed class NotificationService
{
    private readonly CommunityDatabase _database;

    public NotificationService(CommunityDatabase database)
    {
        _database = database;
    }

    public async Task CreateAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid userId,
        NotificationType type,
        string title,
        string message,
        long? threadId,
        long? postId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var retention = Math.Max(1, Plugin.Instance?.Configuration.NotificationRetentionDays ?? 90);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO notifications(user_id, type, title, message, thread_id, post_id, is_read, created_utc, expires_utc)
            VALUES($userId, $type, $title, $message, $threadId, $postId, 0, $created, $expires);
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        command.Parameters.AddWithValue("$type", (int)type);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$threadId", threadId is null ? DBNull.Value : threadId.Value);
        command.Parameters.AddWithValue("$postId", postId is null ? DBNull.Value : postId.Value);
        command.Parameters.AddWithValue("$created", Format(now));
        command.Parameters.AddWithValue("$expires", Format(now.AddDays(retention)));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PagedResult<NotificationDto>> GetAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = ClampPageSize(pageSize);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM notifications WHERE user_id = $userId AND expires_utc > $now;";
        count.Parameters.AddWithValue("$userId", userId.ToString("D"));
        count.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
        var total = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, type, title, message, thread_id, post_id, is_read, created_utc
            FROM notifications
            WHERE user_id = $userId AND expires_utc > $now
            ORDER BY created_utc DESC
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        var items = new List<NotificationDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new NotificationDto(
                reader.GetInt64(0),
                (NotificationType)reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                reader.GetInt64(6) != 0,
                ParseDate(reader.GetString(7))));
        }

        return new PagedResult<NotificationDto>(items, page, pageSize, total);
    }

    public async Task MarkReadAsync(Guid userId, IReadOnlyList<long> notificationIds, bool markAll, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        if (markAll)
        {
            command.CommandText = "UPDATE notifications SET is_read = 1 WHERE user_id = $userId;";
        }
        else
        {
            if (notificationIds.Count == 0)
            {
                return;
            }

            var names = new List<string>();
            for (var i = 0; i < notificationIds.Count; i++)
            {
                var name = "$id" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                names.Add(name);
                command.Parameters.AddWithValue(name, notificationIds[i]);
            }

            command.CommandText = $"UPDATE notifications SET is_read = 1 WHERE user_id = $userId AND id IN ({string.Join(',', names)});";
        }

        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static int ClampPageSize(int pageSize)
    {
        var defaultSize = Math.Max(1, Plugin.Instance?.Configuration.DefaultPageSize ?? 25);
        var max = Math.Max(defaultSize, Plugin.Instance?.Configuration.MaximumPageSize ?? 100);
        return Math.Clamp(pageSize <= 0 ? defaultSize : pageSize, 1, max);
    }

    private static string Format(DateTime value) => value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTime ParseDate(string value) => DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
}
