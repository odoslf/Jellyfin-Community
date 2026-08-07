using System.Text.Json;
using Jellyfin.Plugin.Community.Domain;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Community.Services;

public sealed partial class CommunityService
{
    public async Task<long> ReportPostAsync(CommunityUserContext user, long postId, ReportRequest request, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanRead(user);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 100)
        {
            throw new CommunityValidationException("Report reason must contain between 1 and 100 characters.");
        }

        if (request.Comment?.Length > 2000)
        {
            throw new CommunityValidationException("Report comment cannot exceed 2000 characters.");
        }

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var post = await GetPostAsync(connection, user, postId, cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO reports(post_id, thread_id, reporter_user_id, reporter_name, reason, comment, state, created_utc, updated_utc)
                VALUES($postId, $threadId, $userId, $username, $reason, $comment, 0, $now, $now);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$postId", postId);
            command.Parameters.AddWithValue("$threadId", post.ThreadId);
            command.Parameters.AddWithValue("$userId", user.UserId.ToString("D"));
            command.Parameters.AddWithValue("$username", user.Username);
            command.Parameters.AddWithValue("$reason", request.Reason.Trim());
            command.Parameters.AddWithValue("$comment", DbValue(request.Comment?.Trim()));
            command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new CommunityValidationException("This user has already reported the post.");
        }
    }

    public async Task<PagedResult<ReportDto>> GetReportsAsync(CommunityUserContext user, ModerationQuery query, CancellationToken cancellationToken)
    {
        _permissions.EnsureModerator(user);
        var page = Math.Max(1, query.Page);
        var pageSize = ClampPageSize(query.PageSize);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var stateWhere = query.State is null ? string.Empty : "WHERE r.state = $state";
        await using var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM reports r {stateWhere};";
        if (query.State is not null)
        {
            count.Parameters.AddWithValue("$state", (int)query.State.Value);
        }

        var total = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT r.id, r.post_id, r.thread_id, r.reporter_user_id, r.reporter_name, r.reason, r.comment,
                   r.state, r.assigned_moderator_user_id, r.resolution, r.created_utc, r.updated_utc
            FROM reports r {stateWhere}
            ORDER BY CASE r.state WHEN 0 THEN 0 WHEN 1 THEN 1 ELSE 2 END, r.created_utc
            LIMIT $limit OFFSET $offset;
            """;
        if (query.State is not null)
        {
            command.Parameters.AddWithValue("$state", (int)query.State.Value);
        }

        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        var items = new List<ReportDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new ReportDto(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), Guid.Parse(reader.GetString(3)), reader.GetString(4),
                reader.GetString(5), GetNullableString(reader, 6), (ReportState)reader.GetInt32(7), ParseNullableGuid(reader, 8),
                GetNullableString(reader, 9), ParseDate(reader.GetString(10)), ParseDate(reader.GetString(11))));
        }

        return new PagedResult<ReportDto>(items, page, pageSize, total);
    }

    public async Task ResolveReportAsync(CommunityUserContext user, long reportId, ResolveReportRequest request, CancellationToken cancellationToken)
    {
        _permissions.EnsureModerator(user);
        if (request.State is ReportState.Open || string.IsNullOrWhiteSpace(request.Resolution) || request.Resolution.Trim().Length > 2000)
        {
            throw new CommunityValidationException("A resolved report requires a final state and a resolution up to 2000 characters.");
        }

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        Guid reporterId;
        long threadId;
        long postId;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = (SqliteTransaction)transaction;
            select.CommandText = "SELECT reporter_user_id, thread_id, post_id FROM reports WHERE id = $id;";
            select.Parameters.AddWithValue("$id", reportId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new CommunityNotFoundException("Report not found.");
            }

            reporterId = Guid.Parse(reader.GetString(0));
            threadId = reader.GetInt64(1);
            postId = reader.GetInt64(2);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                UPDATE reports SET state = $state, assigned_moderator_user_id = $moderator,
                    resolution = $resolution, updated_utc = $now WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$state", (int)request.State);
            command.Parameters.AddWithValue("$moderator", user.UserId.ToString("D"));
            command.Parameters.AddWithValue("$resolution", request.Resolution.Trim());
            command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
            command.Parameters.AddWithValue("$id", reportId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await _notifications.CreateAsync(connection, (SqliteTransaction)transaction, reporterId, NotificationType.ReportResolved, "Report resolved", request.Resolution.Trim(), threadId, postId, cancellationToken).ConfigureAwait(false);
        await AuditAsync(connection, (SqliteTransaction)transaction, user, "report.resolve", "report", reportId.ToString(System.Globalization.CultureInfo.InvariantCulture), request.Resolution, null, JsonSerializer.Serialize(request), cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ModerateThreadAsync(CommunityUserContext user, long threadId, ThreadStateRequest request, CancellationToken cancellationToken)
    {
        _permissions.EnsureModerator(user);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var before = await GetThreadSummaryAsync(connection, user, threadId, cancellationToken).ConfigureAwait(false);
        if (request.MoveToCategoryId is not null)
        {
            await _permissions.EnsureCategoryVisibleAsync(user, request.MoveToCategoryId.Value, cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE threads SET
                is_pinned = COALESCE($pinned, is_pinned),
                is_locked = COALESCE($locked, is_locked),
                is_archived = COALESCE($archived, is_archived),
                is_hidden = COALESCE($hidden, is_hidden),
                category_id = COALESCE($category, category_id),
                approved_utc = CASE WHEN requires_approval = 1 AND approved_utc IS NULL THEN $now ELSE approved_utc END,
                approved_by_user_id = CASE WHEN requires_approval = 1 AND approved_by_user_id IS NULL THEN $moderator ELSE approved_by_user_id END,
                updated_utc = $now
            WHERE id = $id AND deleted_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$pinned", request.IsPinned is null ? DBNull.Value : request.IsPinned.Value ? 1 : 0);
        command.Parameters.AddWithValue("$locked", request.IsLocked is null ? DBNull.Value : request.IsLocked.Value ? 1 : 0);
        command.Parameters.AddWithValue("$archived", request.IsArchived is null ? DBNull.Value : request.IsArchived.Value ? 1 : 0);
        command.Parameters.AddWithValue("$hidden", request.IsHidden is null ? DBNull.Value : request.IsHidden.Value ? 1 : 0);
        command.Parameters.AddWithValue("$category", request.MoveToCategoryId is null ? DBNull.Value : request.MoveToCategoryId.Value);
        command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
        command.Parameters.AddWithValue("$moderator", user.UserId.ToString("D"));
        command.Parameters.AddWithValue("$id", threadId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var after = await GetThreadSummaryAsync(connection, user, threadId, cancellationToken).ConfigureAwait(false);
        await AuditAsync(connection, null, user, "thread.moderate", "thread", threadId.ToString(System.Globalization.CultureInfo.InvariantCulture), request.Reason, JsonSerializer.Serialize(before), JsonSerializer.Serialize(after), cancellationToken).ConfigureAwait(false);
    }

    public async Task SetUserStatusAsync(CommunityUserContext user, Guid targetUserId, UserStatusRequest request, CancellationToken cancellationToken)
    {
        _permissions.EnsureModerator(user);
        if (targetUserId == user.UserId && !user.IsAdministrator)
        {
            throw new CommunityValidationException("A moderator cannot suspend or mute themselves.");
        }

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO user_forum_status(user_id, is_suspended, suspended_until_utc, is_muted, muted_until_utc, reason, updated_by_user_id, updated_utc)
            VALUES($userId, $suspended, $suspendedUntil, $muted, $mutedUntil, $reason, $actor, $now)
            ON CONFLICT(user_id) DO UPDATE SET
                is_suspended = excluded.is_suspended, suspended_until_utc = excluded.suspended_until_utc,
                is_muted = excluded.is_muted, muted_until_utc = excluded.muted_until_utc,
                reason = excluded.reason, updated_by_user_id = excluded.updated_by_user_id, updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$userId", targetUserId.ToString("D"));
        command.Parameters.AddWithValue("$suspended", request.IsSuspended ? 1 : 0);
        command.Parameters.AddWithValue("$suspendedUntil", request.SuspendedUntilUtc is null ? DBNull.Value : Format(request.SuspendedUntilUtc.Value));
        command.Parameters.AddWithValue("$muted", request.IsMuted ? 1 : 0);
        command.Parameters.AddWithValue("$mutedUntil", request.MutedUntilUtc is null ? DBNull.Value : Format(request.MutedUntilUtc.Value));
        command.Parameters.AddWithValue("$reason", DbValue(request.Reason?.Trim()));
        command.Parameters.AddWithValue("$actor", user.UserId.ToString("D"));
        command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync(connection, null, user, "user.status", "user", targetUserId.ToString("D"), request.Reason, null, JsonSerializer.Serialize(request), cancellationToken).ConfigureAwait(false);
    }

    public async Task<PagedResult<AuditEntryDto>> GetAuditAsync(CommunityUserContext user, int page, int pageSize, CancellationToken cancellationToken)
    {
        _permissions.EnsureAdministrator(user);
        page = Math.Max(1, page);
        pageSize = ClampPageSize(pageSize);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM moderation_actions;";
        var total = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, actor_user_id, actor_name, action, entity_type, entity_id, reason, before_json, after_json, created_utc
            FROM moderation_actions ORDER BY created_utc DESC LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        var items = new List<AuditEntryDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new AuditEntryDto(
                reader.GetInt64(0), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), GetNullableString(reader, 6), GetNullableString(reader, 7), GetNullableString(reader, 8), ParseDate(reader.GetString(9))));
        }

        return new PagedResult<AuditEntryDto>(items, page, pageSize, total);
    }
}
