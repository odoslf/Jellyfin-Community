using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Community.Domain;
using Jellyfin.Plugin.Community.Infrastructure;
using MediaBrowser.Controller.Library;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Community.Services;

public sealed partial class CommunityService
{
    private readonly CommunityDatabase _database;
    private readonly PermissionService _permissions;
    private readonly MarkdownService _markdown;
    private readonly RateLimitService _rateLimit;
    private readonly NotificationService _notifications;
    private readonly AttachmentService _attachments;
    private readonly BackupService _backups;
    private readonly IUserDataManager _userDataManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<CommunityService> _logger;

    public CommunityService(
        CommunityDatabase database,
        PermissionService permissions,
        MarkdownService markdown,
        RateLimitService rateLimit,
        NotificationService notifications,
        AttachmentService attachments,
        BackupService backups,
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        ILogger<CommunityService> logger)
    {
        _database = database;
        _permissions = permissions;
        _markdown = markdown;
        _rateLimit = rateLimit;
        _notifications = notifications;
        _attachments = attachments;
        _backups = backups;
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CommunityUserContext user, CancellationToken cancellationToken)
    {
        _permissions.EnsureCanRead(user);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.id, c.name, c.slug, c.description, c.library_id, c.sort_order,
                   c.is_read_only, c.requires_approval, c.is_archived,
                   COUNT(t.id), c.created_utc, c.updated_utc
            FROM categories c
            LEFT JOIN threads t ON t.category_id = c.id AND t.deleted_utc IS NULL AND t.is_hidden = 0
            WHERE c.is_archived = 0 OR $isModerator = 1
            GROUP BY c.id
            ORDER BY c.sort_order, c.name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$isModerator", user.IsModerator ? 1 : 0);
        var categories = new List<CategoryDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var libraryId = ParseNullableGuid(reader, 4);
            if (libraryId is not null)
            {
                try
                {
                    _permissions.EnsureItemVisible(user, libraryId);
                }
                catch (CommunityForbiddenException)
                {
                    continue;
                }
            }

            categories.Add(new CategoryDto(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                GetNullableString(reader, 3),
                libraryId,
                reader.GetInt32(5),
                ReadBool(reader, 6),
                ReadBool(reader, 7),
                ReadBool(reader, 8),
                reader.GetInt64(9),
                ParseDate(reader.GetString(10)),
                ParseDate(reader.GetString(11))));
        }

        return categories;
    }

    public async Task<CategoryDto> CreateCategoryAsync(CommunityUserContext user, CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        _permissions.EnsureAdministrator(user);
        ValidateCategory(request.Name, request.Description);
        _permissions.EnsureItemVisible(user, request.LibraryId);
        var now = DateTime.UtcNow;
        var slug = Slugify(request.Name);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO categories(name, slug, description, library_id, sort_order, is_read_only, requires_approval, created_utc, updated_utc)
            VALUES($name, $slug, $description, $libraryId, $sortOrder, $readOnly, $approval, $now, $now);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", request.Name.Trim());
        command.Parameters.AddWithValue("$slug", await GetUniqueSlugAsync(connection, slug, null, cancellationToken).ConfigureAwait(false));
        command.Parameters.AddWithValue("$description", DbValue(request.Description?.Trim()));
        command.Parameters.AddWithValue("$libraryId", DbValue(request.LibraryId?.ToString("D")));
        command.Parameters.AddWithValue("$sortOrder", request.SortOrder);
        command.Parameters.AddWithValue("$readOnly", request.IsReadOnly ? 1 : 0);
        command.Parameters.AddWithValue("$approval", request.RequiresApproval ? 1 : 0);
        command.Parameters.AddWithValue("$now", Format(now));
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        await AuditAsync(connection, null, user, "category.create", "category", id.ToString(System.Globalization.CultureInfo.InvariantCulture), null, null, JsonSerializer.Serialize(request), cancellationToken).ConfigureAwait(false);
        return (await GetCategoriesAsync(user, cancellationToken).ConfigureAwait(false)).Single(category => category.Id == id);
    }

    public async Task<CategoryDto> UpdateCategoryAsync(CommunityUserContext user, long id, UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        _permissions.EnsureAdministrator(user);
        ValidateCategory(request.Name, request.Description);
        _permissions.EnsureItemVisible(user, request.LibraryId);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var before = await GetCategoryJsonAsync(connection, id, cancellationToken).ConfigureAwait(false);
        var slug = await GetUniqueSlugAsync(connection, Slugify(request.Name), id, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE categories SET name = $name, slug = $slug, description = $description, library_id = $libraryId,
                sort_order = $sortOrder, is_read_only = $readOnly, requires_approval = $approval,
                is_archived = $archived, updated_utc = $now
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", request.Name.Trim());
        command.Parameters.AddWithValue("$slug", slug);
        command.Parameters.AddWithValue("$description", DbValue(request.Description?.Trim()));
        command.Parameters.AddWithValue("$libraryId", DbValue(request.LibraryId?.ToString("D")));
        command.Parameters.AddWithValue("$sortOrder", request.SortOrder);
        command.Parameters.AddWithValue("$readOnly", request.IsReadOnly ? 1 : 0);
        command.Parameters.AddWithValue("$approval", request.RequiresApproval ? 1 : 0);
        command.Parameters.AddWithValue("$archived", request.IsArchived ? 1 : 0);
        command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            throw new CommunityNotFoundException("Category not found.");
        }

        var after = await GetCategoryJsonAsync(connection, id, cancellationToken).ConfigureAwait(false);
        await AuditAsync(connection, null, user, "category.update", "category", id.ToString(System.Globalization.CultureInfo.InvariantCulture), null, before, after, cancellationToken).ConfigureAwait(false);
        return (await GetCategoriesAsync(user, cancellationToken).ConfigureAwait(false)).Single(category => category.Id == id);
    }

    public async Task DeleteCategoryAsync(CommunityUserContext user, long id, CancellationToken cancellationToken)
    {
        _permissions.EnsureAdministrator(user);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM threads WHERE category_id = $id AND deleted_utc IS NULL;";
        count.Parameters.AddWithValue("$id", id);
        if (Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) > 0)
        {
            throw new CommunityValidationException("A non-empty category must be archived or its threads moved before deletion.");
        }

        var before = await GetCategoryJsonAsync(connection, id, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM categories WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            throw new CommunityNotFoundException("Category not found.");
        }

        await AuditAsync(connection, null, user, "category.delete", "category", id.ToString(System.Globalization.CultureInfo.InvariantCulture), null, before, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetModeratorAsync(CommunityUserContext user, Guid targetUserId, bool enabled, long? categoryId, CancellationToken cancellationToken)
    {
        _permissions.EnsureAdministrator(user);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (enabled)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO forum_roles(user_id, role, category_id, created_utc)
                VALUES($userId, 'moderator', $categoryId, $now)
                ON CONFLICT(user_id) DO UPDATE SET category_id = excluded.category_id;
                """;
            command.Parameters.AddWithValue("$userId", targetUserId.ToString("D"));
            command.Parameters.AddWithValue("$categoryId", categoryId is null ? DBNull.Value : categoryId.Value);
            command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM forum_roles WHERE user_id = $userId;";
            command.Parameters.AddWithValue("$userId", targetUserId.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await AuditAsync(connection, null, user, enabled ? "moderator.add" : "moderator.remove", "user", targetUserId.ToString("D"), null, null, null, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateCategory(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
        {
            throw new CommunityValidationException("Category name must contain between 1 and 100 characters.");
        }

        if (description?.Length > 2000)
        {
            throw new CommunityValidationException("Category description cannot exceed 2000 characters.");
        }
    }

    private static async Task<string> GetUniqueSlugAsync(SqliteConnection connection, string baseSlug, long? excludedId, CancellationToken cancellationToken)
    {
        for (var suffix = 0; suffix < 10_000; suffix++)
        {
            var candidate = suffix == 0 ? baseSlug : baseSlug + "-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM categories WHERE slug = $slug COLLATE NOCASE AND ($excluded IS NULL OR id <> $excluded);";
            command.Parameters.AddWithValue("$slug", candidate);
            command.Parameters.AddWithValue("$excluded", excludedId is null ? DBNull.Value : excludedId.Value);
            var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            if (count == 0)
            {
                return candidate;
            }
        }

        throw new CommunityValidationException("Unable to create a unique category slug.");
    }

    private static string Slugify(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var dash = false;
        foreach (var character in normalized)
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                dash = false;
            }
            else if (!dash && builder.Length > 0)
            {
                builder.Append('-');
                dash = true;
            }
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? "category" : result;
    }

    private static int ClampPageSize(int pageSize)
    {
        var defaultSize = Math.Max(1, Plugin.Instance?.Configuration.DefaultPageSize ?? 25);
        var max = Math.Max(defaultSize, Plugin.Instance?.Configuration.MaximumPageSize ?? 100);
        return Math.Clamp(pageSize <= 0 ? defaultSize : pageSize, 1, max);
    }

    private static async Task AuditAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CommunityUserContext actor,
        string action,
        string entityType,
        string entityId,
        string? reason,
        string? beforeJson,
        string? afterJson,
        CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.LogModerationActions == false)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO moderation_actions(actor_user_id, actor_name, action, entity_type, entity_id, reason, before_json, after_json, created_utc)
            VALUES($actorId, $actorName, $action, $entityType, $entityId, $reason, $before, $after, $now);
            """;
        command.Parameters.AddWithValue("$actorId", actor.UserId.ToString("D"));
        command.Parameters.AddWithValue("$actorName", actor.Username);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$entityType", entityType);
        command.Parameters.AddWithValue("$entityId", entityId);
        command.Parameters.AddWithValue("$reason", DbValue(reason));
        command.Parameters.AddWithValue("$before", DbValue(beforeJson));
        command.Parameters.AddWithValue("$after", DbValue(afterJson));
        command.Parameters.AddWithValue("$now", Format(DateTime.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> GetCategoryJsonAsync(SqliteConnection connection, long id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, slug, description, library_id, sort_order, is_read_only, requires_approval, is_archived FROM categories WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return JsonSerializer.Serialize(new
        {
            name = reader.GetString(0),
            slug = reader.GetString(1),
            description = GetNullableString(reader, 2),
            libraryId = GetNullableString(reader, 3),
            sortOrder = reader.GetInt32(4),
            isReadOnly = ReadBool(reader, 5),
            requiresApproval = ReadBool(reader, 6),
            isArchived = ReadBool(reader, 7)
        });
    }

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static bool ReadBool(SqliteDataReader reader, int ordinal) => reader.GetInt64(ordinal) != 0;

    private static string? GetNullableString(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static Guid? ParseNullableGuid(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));

    private static DateTime ParseDate(string value) => DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);

    private static DateTime? ParseNullableDate(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));

    private static string Format(DateTime value) => value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    [GeneratedRegex("[^\\p{L}\\p{N}_.-]", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTagRegex();
}
