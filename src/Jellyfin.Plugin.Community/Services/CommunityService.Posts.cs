using System.Text.Json;
using Jellyfin.Plugin.Community.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Community.Services;

public sealed partial class CommunityService
{
    public async Task<PagedResult<PostDto>> GetPostsAsync(
        CommunityUserContext user,
        long threadId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        _permissions.EnsureCanRead(user);
        page = Math.Max(1, page);
        pageSize = ClampPageSize(pageSize);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var thread = await GetThreadSummaryAsync(connection, user, threadId, cancellationToken).ConfigureAwait(false);
        _permissions.EnsureItemVisible(user, thread.ItemId);
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM posts WHERE thread_id = $threadId AND ($moderator = 1 OR is_hidden = 0);";
        count.Parameters.AddWithValue("$threadId", threadId);
        count.Parameters.AddWithValue("$moderator", user.IsModerator ? 1 : 0);
        var total = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id FROM posts
            WHERE thread_id = $threadId AND ($moderator = 1 OR is_hidden = 0)
            ORDER BY created_utc
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$threadId", threadId);
        command.Parameters.AddWithValue("$moderator", user.IsModerator ? 1 : 0);
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        var ids = new List<long>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ids.Add(reader.GetInt64(0));
            }
        }

        var posts = new List<PostDto>(ids.Count);
        foreach (var id in ids)
        {
            posts.Add(await GetPostAsync(connection, user, id, cancellationToken).ConfigureAwait(false));
        }

        return new PagedResult<PostDto>(posts, page, pageSize, total);
    }

    public async Task<PostDto> CreatePostAsync(CommunityUserContext user, long threadId, CreatePostRequest request, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanWrite(user);
        _rateLimit.CheckReply(user);
        _markdown.ValidateBody(request.Body);
        _permissions.EnsureItemVisible(user, request.SpoilerItemId);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var thread = await GetThreadSummaryAsync(connection, user, threadId, cancellationToken).ConfigureAwait(false);
        if (thread.IsLocked && !user.IsModerator)
        {
            throw new CommunityForbiddenException("The thread is locked.");
        }

        if (thread.IsArchived && !user.IsModerator)
        {
            throw new CommunityForbiddenException("The thread is archived.");
        }

        if (request.ParentPostId is not null)
        {
            await EnsureParentPostInThreadAsync(connection, request.ParentPostId.Value, threadId, cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var postId = await InsertPostAsync(
            connection,
            (SqliteTransaction)transaction,
            user,
            threadId,
            request.ParentPostId,
            request.Body,
            request.ContainsSpoiler,
            request.SpoilerItemId,
            request.SpoilerLabel,
            now,
            cancellationToken).ConfigureAwait(false);

        await using (var updateThread = connection.CreateCommand())
        {
            updateThread.Transaction = (SqliteTransaction)transaction;
            updateThread.CommandText = "UPDATE threads SET reply_count = reply_count + 1, updated_utc = $now, last_activity_utc = $now WHERE id = $threadId;";
            updateThread.Parameters.AddWithValue("$now", Format(now));
            updateThread.Parameters.AddWithValue("$threadId", threadId);
            await updateThread.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await NotifyThreadParticipantsAsync(connection, (SqliteTransaction)transaction, user, thread, postId, request, cancellationToken).ConfigureAwait(false);
        await AuditAsync(connection, (SqliteTransaction)transaction, user, "post.create", "post", postId.ToString(System.Globalization.CultureInfo.InvariantCulture), null, null, null, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await NotifyMentionsBestEffortAsync(connection, user, request.Body, threadId, postId, thread.Title, cancellationToken).ConfigureAwait(false);
        return await GetPostAsync(connection, user, postId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PostDto> UpdatePostAsync(CommunityUserContext user, long postId, UpdatePostRequest request, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanWrite(user);
        _markdown.ValidateBody(request.Body);
        _permissions.EnsureItemVisible(user, request.SpoilerItemId);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var before = await GetPostAsync(connection, user, postId, cancellationToken).ConfigureAwait(false);
        if (!before.CanEdit)
        {
            throw new CommunityForbiddenException("The post cannot be edited by this user.");
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var history = connection.CreateCommand())
        {
            history.Transaction = (SqliteTransaction)transaction;
            history.CommandText = """
                INSERT INTO post_edits(post_id, editor_user_id, old_body_markdown, new_body_markdown, reason, created_utc)
                VALUES($postId, $editor, $oldBody, $newBody, $reason, $now);
                """;
            history.Parameters.AddWithValue("$postId", postId);
            history.Parameters.AddWithValue("$editor", user.UserId.ToString("D"));
            history.Parameters.AddWithValue("$oldBody", before.BodyMarkdown);
            history.Parameters.AddWithValue("$newBody", request.Body.Trim());
            history.Parameters.AddWithValue("$reason", DbValue(request.EditReason?.Trim()));
            history.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
            await history.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                UPDATE posts SET body_markdown = $body, body_html = $html, contains_spoiler = $spoiler,
                    spoiler_item_id = $spoilerItem, spoiler_label = $spoilerLabel, is_edited = 1, updated_utc = $now
                WHERE id = $postId AND is_deleted = 0;
                """;
            command.Parameters.AddWithValue("$body", request.Body.Trim());
            command.Parameters.AddWithValue("$html", _markdown.Render(request.Body));
            command.Parameters.AddWithValue("$spoiler", request.ContainsSpoiler ? 1 : 0);
            command.Parameters.AddWithValue("$spoilerItem", DbValue(request.SpoilerItemId?.ToString("D")));
            command.Parameters.AddWithValue("$spoilerLabel", DbValue(request.SpoilerLabel?.Trim()));
            command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
            command.Parameters.AddWithValue("$postId", postId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await AuditAsync(connection, (SqliteTransaction)transaction, user, "post.update", "post", postId.ToString(System.Globalization.CultureInfo.InvariantCulture), request.EditReason, JsonSerializer.Serialize(before), null, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await NotifyMentionsBestEffortAsync(connection, user, request.Body, before.ThreadId, postId, "Edited post", cancellationToken).ConfigureAwait(false);
        return await GetPostAsync(connection, user, postId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeletePostAsync(CommunityUserContext user, long postId, string? reason, bool permanent, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanWrite(user);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var before = await GetPostAsync(connection, user, postId, cancellationToken).ConfigureAwait(false);
        if (!before.CanEdit)
        {
            throw new CommunityForbiddenException("The post cannot be deleted by this user.");
        }

        if (permanent && !user.IsAdministrator)
        {
            throw new CommunityForbiddenException("Permanent deletion requires Jellyfin administrator permission.");
        }

        var isFirstPost = await IsFirstPostAsync(connection, before.ThreadId, postId, cancellationToken).ConfigureAwait(false);
        if (isFirstPost)
        {
            await DeleteThreadAsync(user, before.ThreadId, reason, permanent, cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            if (permanent)
            {
                command.CommandText = "DELETE FROM posts WHERE id = $id;";
            }
            else
            {
                command.CommandText = """
                    UPDATE posts SET is_deleted = 1, body_markdown = '[deleted]', body_html = '<p>[deleted]</p>',
                        deleted_utc = $now, updated_utc = $now WHERE id = $id;
                    """;
                command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
            }

            command.Parameters.AddWithValue("$id", postId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var updateThread = connection.CreateCommand())
        {
            updateThread.Transaction = (SqliteTransaction)transaction;
            updateThread.CommandText = "UPDATE threads SET reply_count = MAX(0, reply_count - 1), updated_utc = $now WHERE id = $threadId;";
            updateThread.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
            updateThread.Parameters.AddWithValue("$threadId", before.ThreadId);
            await updateThread.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await AuditAsync(connection, (SqliteTransaction)transaction, user, permanent ? "post.delete.permanent" : "post.delete", "post", postId.ToString(System.Globalization.CultureInfo.InvariantCulture), reason, JsonSerializer.Serialize(before), null, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetReactionAsync(CommunityUserContext user, long postId, string? reaction, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanWrite(user);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var post = await GetPostAsync(connection, user, postId, cancellationToken).ConfigureAwait(false);
        if (post.IsDeleted)
        {
            throw new CommunityValidationException("Deleted posts cannot receive reactions.");
        }

        await using var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(reaction))
        {
            command.CommandText = "DELETE FROM reactions WHERE post_id = $postId AND user_id = $userId;";
        }
        else
        {
            var allowed = Plugin.Instance?.Configuration.AllowedReactions ?? [];
            if (!allowed.Contains(reaction, StringComparer.OrdinalIgnoreCase))
            {
                throw new CommunityValidationException("The reaction is not allowed by this server.");
            }

            command.CommandText = """
                INSERT INTO reactions(post_id, user_id, reaction, created_utc)
                VALUES($postId, $userId, $reaction, $now)
                ON CONFLICT(post_id, user_id) DO UPDATE SET reaction = excluded.reaction, created_utc = excluded.created_utc;
                """;
            command.Parameters.AddWithValue("$reaction", reaction.ToLowerInvariant());
            command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
        }

        command.Parameters.AddWithValue("$postId", postId);
        command.Parameters.AddWithValue("$userId", user.UserId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<PostDto> GetPostAsync(SqliteConnection connection, CommunityUserContext user, long postId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.thread_id, p.parent_post_id, p.author_user_id, p.author_name,
                   p.body_markdown, p.body_html, p.contains_spoiler, p.spoiler_item_id, p.spoiler_label,
                   p.is_edited, p.is_hidden, p.is_deleted, p.created_utc, p.updated_utc,
                   t.item_id, t.is_hidden, t.deleted_utc
            FROM posts p
            JOIN threads t ON t.id = p.thread_id
            WHERE p.id = $id;
            """;
        command.Parameters.AddWithValue("$id", postId);

        long id;
        long threadId;
        long? parentPostId;
        Guid authorId;
        string authorName;
        string bodyMarkdown;
        string bodyHtml;
        bool containsSpoiler;
        Guid? spoilerItem;
        string? spoilerLabel;
        bool isEdited;
        bool hidden;
        bool isDeleted;
        DateTime created;
        DateTime updated;
        Guid? threadItem;
        bool threadHidden;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new CommunityNotFoundException("Post not found.");
            }

            if (!reader.IsDBNull(17))
            {
                throw new CommunityNotFoundException("Post not found.");
            }

            id = reader.GetInt64(0);
            threadId = reader.GetInt64(1);
            parentPostId = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            authorId = Guid.Parse(reader.GetString(3));
            authorName = reader.GetString(4);
            bodyMarkdown = reader.GetString(5);
            bodyHtml = reader.GetString(6);
            containsSpoiler = ReadBool(reader, 7);
            spoilerItem = ParseNullableGuid(reader, 8);
            spoilerLabel = GetNullableString(reader, 9);
            isEdited = ReadBool(reader, 10);
            hidden = ReadBool(reader, 11);
            isDeleted = ReadBool(reader, 12);
            created = ParseDate(reader.GetString(13));
            updated = ParseDate(reader.GetString(14));
            threadItem = ParseNullableGuid(reader, 15);
            threadHidden = ReadBool(reader, 16);
        }

        _permissions.EnsureItemVisible(user, threadItem);
        if ((hidden || threadHidden) && !user.IsModerator && authorId != user.UserId)
        {
            throw new CommunityNotFoundException("Post not found.");
        }

        var spoilerUnlocked = await IsSpoilerUnlockedAsync(user, containsSpoiler, spoilerItem, cancellationToken).ConfigureAwait(false);
        var reactions = await GetReactionsAsync(connection, postId, user.UserId, cancellationToken).ConfigureAwait(false);
        var attachments = await GetAttachmentsAsync(connection, postId, cancellationToken).ConfigureAwait(false);
        return new PostDto(
            id,
            threadId,
            parentPostId,
            authorId,
            authorName,
            bodyMarkdown,
            bodyHtml,
            containsSpoiler,
            spoilerItem,
            spoilerLabel,
            spoilerUnlocked,
            isEdited,
            hidden,
            isDeleted,
            created,
            updated,
            reactions.Counts,
            reactions.Current,
            attachments,
            PermissionService.CanEditPost(user, authorId, created),
            user.IsModerator);
    }

    private async Task<bool> IsSpoilerUnlockedAsync(CommunityUserContext user, bool containsSpoiler, Guid? spoilerItemId, CancellationToken cancellationToken)
    {
        if (!containsSpoiler || Plugin.Instance?.Configuration.EnableSmartSpoilers != true)
        {
            return true;
        }

        if (spoilerItemId is null)
        {
            return false;
        }

        var item = _libraryManager.GetItemById(spoilerItemId.Value);
        if (item is null || !item.IsVisible(user.User))
        {
            return false;
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return _userDataManager.GetUserData(user.User, item)?.Played == true;
    }

    private async Task NotifyThreadParticipantsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CommunityUserContext author,
        ThreadSummaryDto thread,
        long postId,
        CreatePostRequest request,
        CancellationToken cancellationToken)
    {
        var recipients = new HashSet<Guid>();
        if (thread.AuthorUserId != author.UserId)
        {
            recipients.Add(thread.AuthorUserId);
        }

        await using (var followers = connection.CreateCommand())
        {
            followers.Transaction = transaction;
            followers.CommandText = "SELECT user_id FROM thread_follows WHERE thread_id = $threadId AND user_id <> $authorId;";
            followers.Parameters.AddWithValue("$threadId", thread.Id);
            followers.Parameters.AddWithValue("$authorId", author.UserId.ToString("D"));
            await using var reader = await followers.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (Guid.TryParse(reader.GetString(0), out var id))
                {
                    recipients.Add(id);
                }
            }
        }

        if (request.ParentPostId is not null)
        {
            await using var parent = connection.CreateCommand();
            parent.Transaction = transaction;
            parent.CommandText = "SELECT author_user_id FROM posts WHERE id = $id;";
            parent.Parameters.AddWithValue("$id", request.ParentPostId.Value);
            var parentAuthor = Convert.ToString(await parent.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            if (Guid.TryParse(parentAuthor, out var parentId) && parentId != author.UserId)
            {
                recipients.Add(parentId);
            }
        }

        foreach (var recipient in recipients)
        {
            var type = request.ParentPostId is null ? NotificationType.FollowedThread : NotificationType.Reply;
            await _notifications.CreateAsync(connection, transaction, recipient, type, thread.Title, $"{author.Username} added a reply.", thread.Id, postId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task NotifyMentionsBestEffortAsync(
        SqliteConnection connection,
        CommunityUserContext author,
        string body,
        long threadId,
        long postId,
        string threadTitle,
        CancellationToken cancellationToken)
    {
        try
        {
            await NotifyMentionsAsync(connection, null, author, body, threadId, postId, threadTitle, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The post is already committed. A secondary notification failure must
            // never make the client retry and create a duplicate conversation/post.
            _logger.LogWarning(
                exception,
                "Community saved post {PostId} in thread {ThreadId}, but mention notifications could not be completed.",
                postId,
                threadId);
        }
    }

    private async Task NotifyMentionsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CommunityUserContext author,
        string body,
        long threadId,
        long postId,
        string threadTitle,
        CancellationToken cancellationToken)
    {
        foreach (var mention in _markdown.ExtractMentions(body))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT user_id FROM known_users WHERE username = $username COLLATE NOCASE AND is_deleted = 0;";
            command.Parameters.AddWithValue("$username", mention);
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is string raw && Guid.TryParse(raw, out var recipient) && recipient != author.UserId)
            {
                await _notifications.CreateAsync(connection, transaction, recipient, NotificationType.Mention, threadTitle, $"{author.Username} mentioned you.", threadId, postId, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<long> InsertPostAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CommunityUserContext user,
        long threadId,
        long? parentPostId,
        string body,
        bool containsSpoiler,
        Guid? spoilerItemId,
        string? spoilerLabel,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO posts(thread_id, parent_post_id, author_user_id, author_name, body_markdown, body_html,
                contains_spoiler, spoiler_item_id, spoiler_label, created_utc, updated_utc)
            VALUES($threadId, $parentId, $authorId, $authorName, $markdown, $html,
                $spoiler, $spoilerItem, $spoilerLabel, $now, $now);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$threadId", threadId);
        command.Parameters.AddWithValue("$parentId", parentPostId is null ? DBNull.Value : parentPostId.Value);
        command.Parameters.AddWithValue("$authorId", user.UserId.ToString("D"));
        command.Parameters.AddWithValue("$authorName", user.Username);
        command.Parameters.AddWithValue("$markdown", body.Trim());
        command.Parameters.AddWithValue("$html", _markdown.Render(body));
        command.Parameters.AddWithValue("$spoiler", containsSpoiler ? 1 : 0);
        command.Parameters.AddWithValue("$spoilerItem", DbValue(spoilerItemId?.ToString("D")));
        command.Parameters.AddWithValue("$spoilerLabel", DbValue(spoilerLabel?.Trim()));
        command.Parameters.AddWithValue("$now", Format(now));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task EnsureParentPostInThreadAsync(SqliteConnection connection, long parentPostId, long threadId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM posts WHERE id = $postId AND thread_id = $threadId AND is_deleted = 0;";
        command.Parameters.AddWithValue("$postId", parentPostId);
        command.Parameters.AddWithValue("$threadId", threadId);
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 0)
        {
            throw new CommunityValidationException("The quoted or parent post does not belong to this thread.");
        }
    }

    private static async Task<bool> IsFirstPostAsync(SqliteConnection connection, long threadId, long postId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM posts WHERE thread_id = $threadId ORDER BY created_utc LIMIT 1;";
        command.Parameters.AddWithValue("$threadId", threadId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is not null && Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) == postId;
    }

    private static async Task<(IReadOnlyDictionary<string, int> Counts, string? Current)> GetReactionsAsync(SqliteConnection connection, long postId, Guid userId, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT reaction, COUNT(*), MAX(CASE WHEN user_id = $userId THEN reaction ELSE NULL END) FROM reactions WHERE post_id = $postId GROUP BY reaction;";
        command.Parameters.AddWithValue("$postId", postId);
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var reaction = reader.GetString(0);
            counts[reaction] = reader.GetInt32(1);
            if (!reader.IsDBNull(2))
            {
                current = reader.GetString(2);
            }
        }

        return (counts, current);
    }

    private static async Task<IReadOnlyList<AttachmentDto>> GetAttachmentsAsync(SqliteConnection connection, long postId, CancellationToken cancellationToken)
    {
        var items = new List<AttachmentDto>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, original_name, media_type, size_bytes, created_utc FROM attachments WHERE post_id = $postId AND deleted_utc IS NULL ORDER BY id;";
        command.Parameters.AddWithValue("$postId", postId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetInt64(0);
            items.Add(new AttachmentDto(id, postId, reader.GetString(1), reader.GetString(2), reader.GetInt64(3), $"Community/api/v1/attachments/{id}", ParseDate(reader.GetString(4))));
        }

        return items;
    }
}
