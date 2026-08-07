using Jellyfin.Plugin.Community.Domain;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Community.Services;

public sealed partial class CommunityService
{
    public async Task SaveDraftAsync(CommunityUserContext user, DraftRequest request, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanWrite(user);
        if (string.IsNullOrWhiteSpace(request.Key) || request.Key.Length > 100 || request.Body.Length > Math.Max(1, Plugin.Instance?.Configuration.MaxPostLength ?? 20_000))
        {
            throw new CommunityValidationException("Invalid draft key or body.");
        }

        if (request.MetadataJson?.Length > 5000)
        {
            throw new CommunityValidationException("Draft metadata is too large.");
        }

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO drafts(user_id, draft_key, body, metadata_json, updated_utc)
            VALUES($userId, $key, $body, $metadata, $now)
            ON CONFLICT(user_id, draft_key) DO UPDATE SET body = excluded.body, metadata_json = excluded.metadata_json, updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$userId", user.UserId.ToString("D"));
        command.Parameters.AddWithValue("$key", request.Key);
        command.Parameters.AddWithValue("$body", request.Body);
        command.Parameters.AddWithValue("$metadata", DbValue(request.MetadataJson));
        command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DraftDto?> GetDraftAsync(CommunityUserContext user, string key, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanRead(user);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT body, metadata_json, updated_utc FROM drafts WHERE user_id = $userId AND draft_key = $key;";
        command.Parameters.AddWithValue("$userId", user.UserId.ToString("D"));
        command.Parameters.AddWithValue("$key", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new DraftDto(key, reader.GetString(0), GetNullableString(reader, 1), ParseDate(reader.GetString(2)));
    }

    public async Task DeleteDraftAsync(CommunityUserContext user, string key, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanRead(user);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM drafts WHERE user_id = $userId AND draft_key = $key;";
        command.Parameters.AddWithValue("$userId", user.UserId.ToString("D"));
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<StorageStatsDto> GetStatsAsync(CommunityUserContext user, CancellationToken cancellationToken)
    {
        _permissions.EnsureAdministrator(user);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM categories),
                (SELECT COUNT(*) FROM threads WHERE deleted_utc IS NULL),
                (SELECT COUNT(*) FROM posts WHERE is_deleted = 0),
                (SELECT COUNT(*) FROM known_users WHERE is_deleted = 0),
                (SELECT COUNT(*) FROM reports WHERE state IN (0, 1));
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new StorageStatsDto(
            _database.GetDatabaseSizeBytes(),
            _attachments.GetAttachmentStorageBytes(),
            _backups.GetBackupStorageBytes(),
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            DateTime.UtcNow);
    }

    public async Task<(int Notifications, int Drafts, int Attachments)> CleanupAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var draftsCutoff = now.AddDays(-Math.Max(1, Plugin.Instance?.Configuration.DraftRetentionDays ?? 30));
        int notifications;
        int drafts;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM notifications WHERE expires_utc < $now;";
            command.Parameters.AddWithValue("$now", Format(now));
            notifications = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM drafts WHERE updated_utc < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", Format(draftsCutoff));
            drafts = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var attachments = await _attachments.PurgeDeletedAsync(cancellationToken).ConfigureAwait(false);
        await _database.OptimizeAsync(cancellationToken).ConfigureAwait(false);
        return (notifications, drafts, attachments);
    }

    public Task<string> CreateBackupAsync(CommunityUserContext user, CancellationToken cancellationToken)
    {
        _permissions.EnsureAdministrator(user);
        return _backups.CreateBackupAsync(cancellationToken);
    }

    public async Task StageRestoreAsync(CommunityUserContext user, Stream archive, CancellationToken cancellationToken)
    {
        _permissions.EnsureAdministrator(user);
        await _backups.StageRestoreAsync(archive, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> IntegrityCheckAsync(CommunityUserContext user, CancellationToken cancellationToken)
    {
        _permissions.EnsureAdministrator(user);
        return await _database.IntegrityCheckAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AnonymizeUserAsync(CommunityUserContext user, Guid targetUserId, bool deletePersonalData, CancellationToken cancellationToken)
    {
        _permissions.EnsureAdministrator(user);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var anonymousName = "Deleted user " + targetUserId.ToString("N")[..8];
        foreach (var statement in new[]
        {
            "UPDATE threads SET author_name = $name WHERE author_user_id = $userId;",
            "UPDATE posts SET author_name = $name WHERE author_user_id = $userId;",
            "UPDATE reports SET reporter_name = $name WHERE reporter_user_id = $userId;",
            "UPDATE moderation_actions SET actor_name = $name WHERE actor_user_id = $userId;",
            "UPDATE known_users SET username = $name, is_deleted = 1, last_seen_utc = $now WHERE user_id = $userId;"
        })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = statement;
            command.Parameters.AddWithValue("$name", anonymousName);
            command.Parameters.AddWithValue("$userId", targetUserId.ToString("D"));
            command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (deletePersonalData)
        {
            foreach (var statement in new[]
            {
                "DELETE FROM notifications WHERE user_id = $userId;",
                "DELETE FROM thread_follows WHERE user_id = $userId;",
                "DELETE FROM read_state WHERE user_id = $userId;",
                "DELETE FROM reactions WHERE user_id = $userId;",
                "DELETE FROM poll_votes WHERE user_id = $userId;",
                "DELETE FROM drafts WHERE user_id = $userId;",
                "DELETE FROM forum_roles WHERE user_id = $userId;",
                "DELETE FROM user_forum_status WHERE user_id = $userId;"
            })
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = statement;
                command.Parameters.AddWithValue("$userId", targetUserId.ToString("D"));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await AuditAsync(connection, (SqliteTransaction)transaction, user, "user.anonymize", "user", targetUserId.ToString("D"), deletePersonalData ? "anonymize-and-delete-personal-data" : "anonymize", null, null, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
