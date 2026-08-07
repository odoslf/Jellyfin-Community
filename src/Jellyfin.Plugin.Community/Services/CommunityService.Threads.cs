using System.Text.Json;
using Jellyfin.Plugin.Community.Domain;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Community.Services;

public sealed partial class CommunityService
{
    public async Task<PagedResult<ThreadSummaryDto>> GetThreadsAsync(
        CommunityUserContext user,
        SearchQuery query,
        CancellationToken cancellationToken)
    {
        _permissions.EnsureCanRead(user);
        if (query.CategoryId is not null)
        {
            await _permissions.EnsureCategoryVisibleAsync(user, query.CategoryId.Value, cancellationToken).ConfigureAwait(false);
        }

        _permissions.EnsureItemVisible(user, query.ItemId);
        var page = Math.Max(1, query.Page);
        var pageSize = ClampPageSize(query.PageSize);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var where = new List<string> { "t.deleted_utc IS NULL" };
        if (!user.IsModerator)
        {
            where.Add("t.is_hidden = 0");
            where.Add("(t.requires_approval = 0 OR t.approved_utc IS NOT NULL OR t.author_user_id = $userId)");
            where.Add("c.is_archived = 0");
        }

        if (query.CategoryId is not null)
        {
            where.Add("t.category_id = $categoryId");
        }

        if (query.ItemId is not null)
        {
            where.Add("t.item_id = $itemId");
        }

        if (query.FollowedOnly)
        {
            where.Add("EXISTS(SELECT 1 FROM thread_follows f WHERE f.thread_id = t.id AND f.user_id = $userId)");
        }

        if (query.UnreadOnly)
        {
            where.Add("(r.read_utc IS NULL OR r.read_utc < t.last_activity_utc)");
        }

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            where.Add("(t.title LIKE $search ESCAPE '\\' OR EXISTS(SELECT 1 FROM posts p WHERE p.thread_id = t.id AND p.is_deleted = 0 AND p.body_markdown LIKE $search ESCAPE '\\'))");
        }

        var whereSql = string.Join(" AND ", where);
        await using var count = connection.CreateCommand();
        count.CommandText = $"""
            SELECT COUNT(*)
            FROM threads t
            JOIN categories c ON c.id = t.category_id
            LEFT JOIN read_state r ON r.thread_id = t.id AND r.user_id = $userId
            WHERE {whereSql};
            """;
        AddThreadQueryParameters(count, user, query);
        var total = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);

        var orderBy = query.Sort.ToLowerInvariant() switch
        {
            "created" => "t.created_utc DESC",
            "replies" => "t.reply_count DESC, t.last_activity_utc DESC",
            "views" => "t.view_count DESC, t.last_activity_utc DESC",
            _ => "t.is_pinned DESC, t.last_activity_utc DESC"
        };

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT t.id, t.category_id, c.name, t.kind, t.title, t.author_user_id, t.author_name,
                   t.item_id, t.item_name, t.is_pinned, t.is_locked, t.is_archived, t.is_hidden,
                   EXISTS(SELECT 1 FROM thread_follows f WHERE f.thread_id = t.id AND f.user_id = $userId),
                   t.reply_count, t.view_count, t.created_utc, t.updated_utc, t.last_activity_utc
            FROM threads t
            JOIN categories c ON c.id = t.category_id
            LEFT JOIN read_state r ON r.thread_id = t.id AND r.user_id = $userId
            WHERE {whereSql}
            ORDER BY {orderBy}
            LIMIT $limit OFFSET $offset;
            """;
        AddThreadQueryParameters(command, user, query);
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        var items = new List<ThreadSummaryDto>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var itemId = ParseNullableGuid(reader, 7);
                if (itemId is not null)
                {
                    try
                    {
                        _permissions.EnsureItemVisible(user, itemId);
                    }
                    catch (CommunityForbiddenException)
                    {
                        continue;
                    }
                }

                items.Add(new ThreadSummaryDto(
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    (ThreadKind)reader.GetInt32(3),
                    reader.GetString(4),
                    Guid.Parse(reader.GetString(5)),
                    reader.GetString(6),
                    itemId,
                    GetNullableString(reader, 8),
                    ReadBool(reader, 9),
                    ReadBool(reader, 10),
                    ReadBool(reader, 11),
                    ReadBool(reader, 12),
                    ReadBool(reader, 13),
                    reader.GetInt32(14),
                    reader.GetInt32(15),
                    ParseDate(reader.GetString(16)),
                    ParseDate(reader.GetString(17)),
                    ParseDate(reader.GetString(18)),
                    []));
            }
        }

        for (var index = 0; index < items.Count; index++)
        {
            items[index] = items[index] with { Tags = await GetTagsAsync(connection, items[index].Id, cancellationToken).ConfigureAwait(false) };
        }

        return new PagedResult<ThreadSummaryDto>(items, page, pageSize, total);
    }

    public async Task<ThreadDto> GetThreadAsync(CommunityUserContext user, long threadId, bool incrementView, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanRead(user);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var summary = await GetThreadSummaryAsync(connection, user, threadId, cancellationToken).ConfigureAwait(false);
        _permissions.EnsureItemVisible(user, summary.ItemId);
        if (summary.IsHidden && !user.IsModerator && summary.AuthorUserId != user.UserId)
        {
            throw new CommunityNotFoundException("Thread not found.");
        }

        if (incrementView)
        {
            await using var updateView = connection.CreateCommand();
            updateView.CommandText = "UPDATE threads SET view_count = view_count + 1 WHERE id = $id;";
            updateView.Parameters.AddWithValue("$id", threadId);
            await updateView.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            summary = summary with { ViewCount = summary.ViewCount + 1 };
        }

        await using var first = connection.CreateCommand();
        first.CommandText = "SELECT id FROM posts WHERE thread_id = $threadId AND is_deleted = 0 ORDER BY created_utc LIMIT 1;";
        first.Parameters.AddWithValue("$threadId", threadId);
        var firstIdValue = await first.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (firstIdValue is null || firstIdValue == DBNull.Value)
        {
            throw new CommunityNotFoundException("Thread has no visible first post.");
        }

        var firstPost = await GetPostAsync(connection, user, Convert.ToInt64(firstIdValue, System.Globalization.CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        var poll = await GetPollAsync(connection, user.UserId, threadId, cancellationToken).ConfigureAwait(false);
        return new ThreadDto(
            summary,
            firstPost,
            poll,
            user.IsModerator || summary.AuthorUserId == user.UserId,
            user.IsModerator);
    }

    public async Task<ThreadDto> CreateThreadAsync(CommunityUserContext user, CreateThreadRequest request, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanWrite(user);
        _rateLimit.CheckThreadCreation(user);
        _markdown.Validate(request.Title, request.Body);
        await _permissions.EnsureCategoryVisibleAsync(user, request.CategoryId, cancellationToken).ConfigureAwait(false);
        _permissions.EnsureItemVisible(user, request.ItemId);
        _permissions.EnsureItemVisible(user, request.SpoilerItemId);
        ValidateThreadKind(user, request);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var categoryFlags = await GetCategoryFlagsAsync(connection, (SqliteTransaction)transaction, request.CategoryId, cancellationToken).ConfigureAwait(false);
        if (categoryFlags.ReadOnly && !user.IsModerator)
        {
            throw new CommunityForbiddenException("The category is read-only.");
        }

        var requiresApproval = categoryFlags.RequiresApproval || Plugin.Instance?.Configuration.RequireApprovalForFirstPost == true;
        if (user.IsModerator)
        {
            requiresApproval = false;
        }

        await using var insertThread = connection.CreateCommand();
        insertThread.Transaction = (SqliteTransaction)transaction;
        insertThread.CommandText = """
            INSERT INTO threads(category_id, kind, title, author_user_id, author_name, item_id, item_name,
                requires_approval, approved_utc, approved_by_user_id, created_utc, updated_utc, last_activity_utc)
            VALUES($categoryId, $kind, $title, $authorId, $authorName, $itemId, $itemName,
                $approval, $approvedUtc, $approvedBy, $now, $now, $now);
            SELECT last_insert_rowid();
            """;
        insertThread.Parameters.AddWithValue("$categoryId", request.CategoryId);
        insertThread.Parameters.AddWithValue("$kind", (int)request.Kind);
        insertThread.Parameters.AddWithValue("$title", request.Title.Trim());
        insertThread.Parameters.AddWithValue("$authorId", user.UserId.ToString("D"));
        insertThread.Parameters.AddWithValue("$authorName", user.Username);
        insertThread.Parameters.AddWithValue("$itemId", DbValue(request.ItemId?.ToString("D")));
        insertThread.Parameters.AddWithValue("$itemName", DbValue(request.ItemName?.Trim()));
        insertThread.Parameters.AddWithValue("$approval", requiresApproval ? 1 : 0);
        insertThread.Parameters.AddWithValue("$approvedUtc", requiresApproval ? DBNull.Value : Format(now));
        insertThread.Parameters.AddWithValue("$approvedBy", requiresApproval ? DBNull.Value : user.UserId.ToString("D"));
        insertThread.Parameters.AddWithValue("$now", Format(now));
        var threadId = Convert.ToInt64(await insertThread.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);

        var postId = await InsertPostAsync(
            connection,
            (SqliteTransaction)transaction,
            user,
            threadId,
            null,
            request.Body,
            request.ContainsSpoiler,
            request.SpoilerItemId,
            request.SpoilerLabel,
            now,
            cancellationToken).ConfigureAwait(false);
        await ReplaceTagsAsync(connection, (SqliteTransaction)transaction, threadId, request.Tags, cancellationToken).ConfigureAwait(false);
        if (request.Poll is not null)
        {
            await CreatePollInternalAsync(connection, (SqliteTransaction)transaction, threadId, request.Poll, now, cancellationToken).ConfigureAwait(false);
        }

        await IndexThreadAsync(connection, (SqliteTransaction)transaction, threadId, request.Title, request.Body, user.Username, cancellationToken).ConfigureAwait(false);
        await AuditAsync(connection, (SqliteTransaction)transaction, user, "thread.create", "thread", threadId.ToString(System.Globalization.CultureInfo.InvariantCulture), null, null, JsonSerializer.Serialize(request), cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await NotifyMentionsAsync(connection, null, user, request.Body, threadId, postId, request.Title, cancellationToken).ConfigureAwait(false);
        return await GetThreadAsync(user, threadId, false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ThreadDto> UpdateThreadAsync(CommunityUserContext user, long threadId, UpdateThreadRequest request, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanWrite(user);
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > Math.Max(1, Plugin.Instance?.Configuration.MaxTitleLength ?? 200))
        {
            throw new CommunityValidationException("Invalid thread title.");
        }

        _permissions.EnsureItemVisible(user, request.SpoilerItemId);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var before = await GetThreadSummaryAsync(connection, user, threadId, cancellationToken).ConfigureAwait(false);
        if (!user.IsModerator && before.AuthorUserId != user.UserId)
        {
            throw new CommunityForbiddenException("The thread cannot be edited by this user.");
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "UPDATE threads SET title = $title, updated_utc = $now WHERE id = $id AND deleted_utc IS NULL;";
        command.Parameters.AddWithValue("$title", request.Title.Trim());
        command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
        command.Parameters.AddWithValue("$id", threadId);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            throw new CommunityNotFoundException("Thread not found.");
        }

        await ReplaceTagsAsync(connection, (SqliteTransaction)transaction, threadId, request.Tags, cancellationToken).ConfigureAwait(false);
        await AuditAsync(connection, (SqliteTransaction)transaction, user, "thread.update", "thread", threadId.ToString(System.Globalization.CultureInfo.InvariantCulture), null, JsonSerializer.Serialize(before), JsonSerializer.Serialize(request), cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await GetThreadAsync(user, threadId, false, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteThreadAsync(CommunityUserContext user, long threadId, string? reason, bool permanent, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanWrite(user);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var thread = await GetThreadSummaryAsync(connection, user, threadId, cancellationToken).ConfigureAwait(false);
        if (!user.IsModerator && thread.AuthorUserId != user.UserId)
        {
            throw new CommunityForbiddenException("The thread cannot be deleted by this user.");
        }

        if (permanent && !user.IsAdministrator)
        {
            throw new CommunityForbiddenException("Permanent deletion requires Jellyfin administrator permission.");
        }

        await using var command = connection.CreateCommand();
        if (permanent)
        {
            command.CommandText = "DELETE FROM threads WHERE id = $id;";
        }
        else
        {
            command.CommandText = "UPDATE threads SET deleted_utc = $now, is_hidden = 1, updated_utc = $now WHERE id = $id;";
            command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
        }

        command.Parameters.AddWithValue("$id", threadId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync(connection, null, user, permanent ? "thread.delete.permanent" : "thread.delete", "thread", threadId.ToString(System.Globalization.CultureInfo.InvariantCulture), reason, JsonSerializer.Serialize(thread), null, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetFollowAsync(CommunityUserContext user, long threadId, bool follow, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanRead(user);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await GetThreadSummaryAsync(connection, user, threadId, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        if (follow)
        {
            command.CommandText = "INSERT OR IGNORE INTO thread_follows(thread_id, user_id, created_utc) VALUES($threadId, $userId, $now);";
            command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
        }
        else
        {
            command.CommandText = "DELETE FROM thread_follows WHERE thread_id = $threadId AND user_id = $userId;";
        }

        command.Parameters.AddWithValue("$threadId", threadId);
        command.Parameters.AddWithValue("$userId", user.UserId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkThreadReadAsync(CommunityUserContext user, long threadId, long? lastReadPostId, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanRead(user);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await GetThreadSummaryAsync(connection, user, threadId, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO read_state(thread_id, user_id, last_read_post_id, read_utc)
            VALUES($threadId, $userId, $postId, $now)
            ON CONFLICT(thread_id, user_id) DO UPDATE SET last_read_post_id = excluded.last_read_post_id, read_utc = excluded.read_utc;
            """;
        command.Parameters.AddWithValue("$threadId", threadId);
        command.Parameters.AddWithValue("$userId", user.UserId.ToString("D"));
        command.Parameters.AddWithValue("$postId", lastReadPostId is null ? DBNull.Value : lastReadPostId.Value);
        command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddThreadQueryParameters(SqliteCommand command, CommunityUserContext user, SearchQuery query)
    {
        command.Parameters.AddWithValue("$userId", user.UserId.ToString("D"));
        if (query.CategoryId is not null)
        {
            command.Parameters.AddWithValue("$categoryId", query.CategoryId.Value);
        }

        if (query.ItemId is not null)
        {
            command.Parameters.AddWithValue("$itemId", query.ItemId.Value.ToString("D"));
        }

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            var escaped = query.Query.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
            command.Parameters.AddWithValue("$search", "%" + escaped + "%");
        }
    }

    private async Task<ThreadSummaryDto> GetThreadSummaryAsync(SqliteConnection connection, CommunityUserContext user, long threadId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.id, t.category_id, c.name, t.kind, t.title, t.author_user_id, t.author_name,
                   t.item_id, t.item_name, t.is_pinned, t.is_locked, t.is_archived, t.is_hidden,
                   EXISTS(SELECT 1 FROM thread_follows f WHERE f.thread_id = t.id AND f.user_id = $userId),
                   t.reply_count, t.view_count, t.created_utc, t.updated_utc, t.last_activity_utc,
                   t.requires_approval, t.approved_utc, t.deleted_utc, c.library_id
            FROM threads t
            JOIN categories c ON c.id = t.category_id
            WHERE t.id = $id;
            """;
        command.Parameters.AddWithValue("$userId", user.UserId.ToString("D"));
        command.Parameters.AddWithValue("$id", threadId);
        ThreadSummaryDto result;
        Guid? libraryId;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new CommunityNotFoundException("Thread not found.");
            }

            if (!reader.IsDBNull(21))
            {
                throw new CommunityNotFoundException("Thread not found.");
            }

            var requiresApproval = ReadBool(reader, 19);
            var approved = !reader.IsDBNull(20);
            var authorId = Guid.Parse(reader.GetString(5));
            if (requiresApproval && !approved && !user.IsModerator && authorId != user.UserId)
            {
                throw new CommunityNotFoundException("Thread not found.");
            }

            libraryId = ParseNullableGuid(reader, 22);
            result = new ThreadSummaryDto(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), (ThreadKind)reader.GetInt32(3), reader.GetString(4),
                authorId, reader.GetString(6), ParseNullableGuid(reader, 7), GetNullableString(reader, 8), ReadBool(reader, 9),
                ReadBool(reader, 10), ReadBool(reader, 11), ReadBool(reader, 12), ReadBool(reader, 13), reader.GetInt32(14),
                reader.GetInt32(15), ParseDate(reader.GetString(16)), ParseDate(reader.GetString(17)), ParseDate(reader.GetString(18)), []);
        }

        _permissions.EnsureItemVisible(user, libraryId);
        return result with { Tags = await GetTagsAsync(connection, threadId, cancellationToken).ConfigureAwait(false) };
    }

    private static async Task<(bool ReadOnly, bool RequiresApproval)> GetCategoryFlagsAsync(SqliteConnection connection, SqliteTransaction transaction, long categoryId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT is_read_only, requires_approval FROM categories WHERE id = $id AND is_archived = 0;";
        command.Parameters.AddWithValue("$id", categoryId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new CommunityNotFoundException("Category not found.");
        }

        return (ReadBool(reader, 0), ReadBool(reader, 1));
    }

    private static void ValidateThreadKind(CommunityUserContext user, CreateThreadRequest request)
    {
        if (request.Kind == ThreadKind.Announcement && !user.IsModerator)
        {
            throw new CommunityForbiddenException("Only moderators can create announcements.");
        }

        if (request.Kind == ThreadKind.Poll && request.Poll is null)
        {
            throw new CommunityValidationException("A poll thread requires poll data.");
        }

        if (request.Poll is not null && request.Kind != ThreadKind.Poll)
        {
            throw new CommunityValidationException("Poll data can only be attached to a poll thread.");
        }
    }

    private static async Task ReplaceTagsAsync(SqliteConnection connection, SqliteTransaction transaction, long threadId, IReadOnlyList<string>? tags, CancellationToken cancellationToken)
    {
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM thread_tags WHERE thread_id = $threadId;";
            clear.Parameters.AddWithValue("$threadId", threadId);
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var raw in (tags ?? []).Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(10))
        {
            if (raw.Length > 40 || SafeTagRegex().IsMatch(raw))
            {
                throw new CommunityValidationException("Tags may contain letters, numbers, dots, dashes and underscores, up to 40 characters.");
            }

            var slug = Slugify(raw);
            await using var insertTag = connection.CreateCommand();
            insertTag.Transaction = transaction;
            insertTag.CommandText = "INSERT OR IGNORE INTO tags(name, slug) VALUES($name, $slug);";
            insertTag.Parameters.AddWithValue("$name", raw);
            insertTag.Parameters.AddWithValue("$slug", slug);
            await insertTag.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await using var link = connection.CreateCommand();
            link.Transaction = transaction;
            link.CommandText = "INSERT OR IGNORE INTO thread_tags(thread_id, tag_id) SELECT $threadId, id FROM tags WHERE slug = $slug;";
            link.Parameters.AddWithValue("$threadId", threadId);
            link.Parameters.AddWithValue("$slug", slug);
            await link.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<string>> GetTagsAsync(SqliteConnection connection, long threadId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT g.name FROM tags g JOIN thread_tags tt ON tt.tag_id = g.id WHERE tt.thread_id = $threadId ORDER BY g.name COLLATE NOCASE;";
        command.Parameters.AddWithValue("$threadId", threadId);
        var tags = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tags.Add(reader.GetString(0));
        }

        return tags;
    }

    private static async Task IndexThreadAsync(SqliteConnection connection, SqliteTransaction transaction, long threadId, string title, string body, string author, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO community_fts(entity_type, entity_id, title, body, author) VALUES('thread', $id, $title, $body, $author);";
            command.Parameters.AddWithValue("$id", threadId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$body", body);
            command.Parameters.AddWithValue("$author", author);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            // FTS5 is optional; LIKE search remains available.
        }
    }
}
