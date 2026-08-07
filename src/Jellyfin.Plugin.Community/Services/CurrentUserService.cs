using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Community.Domain;
using Jellyfin.Plugin.Community.Infrastructure;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Community.Services;

public sealed class CurrentUserService
{
    private readonly IAuthorizationContext _authorizationContext;
    private readonly CommunityDatabase _database;

    public CurrentUserService(IAuthorizationContext authorizationContext, CommunityDatabase database)
    {
        _authorizationContext = authorizationContext;
        _database = database;
    }

    public async Task<CommunityUserContext> GetRequiredAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        if (Plugin.Instance?.Configuration.Enabled == false)
        {
            throw new CommunityForbiddenException("Community is disabled by the server administrator.");
        }

        var authorization = await _authorizationContext.GetAuthorizationInfo(httpContext).ConfigureAwait(false);
        if (!authorization.IsAuthenticated || authorization.User is null)
        {
            throw new UnauthorizedAccessException("An authenticated Jellyfin user is required.");
        }

        var user = authorization.User;
        var isAdmin = user.HasPermission(PermissionKind.IsAdministrator);
        var isDisabled = user.HasPermission(PermissionKind.IsDisabled);
        if (isDisabled)
        {
            throw new UnauthorizedAccessException("The Jellyfin user is disabled.");
        }

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        await UpsertKnownUserAsync(connection, user.Id, user.Username, now, cancellationToken).ConfigureAwait(false);

        var configuredModerator = Plugin.Instance?.Configuration.ModeratorUserIds.Any(
            id => Guid.TryParse(id, out var parsed) && parsed == user.Id) == true;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                EXISTS(SELECT 1 FROM forum_roles WHERE user_id = $userId AND role = 'moderator'),
                COALESCE(is_muted, 0), muted_until_utc,
                COALESCE(is_suspended, 0), suspended_until_utc
            FROM (SELECT 1)
            LEFT JOIN user_forum_status ON user_id = $userId;
            """;
        command.Parameters.AddWithValue("$userId", user.Id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var databaseModerator = false;
        var isMuted = false;
        DateTime? mutedUntil = null;
        var isSuspended = false;
        DateTime? suspendedUntil = null;
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            databaseModerator = reader.GetInt64(0) != 0;
            isMuted = reader.GetInt64(1) != 0;
            mutedUntil = ParseNullableDate(reader, 2);
            isSuspended = reader.GetInt64(3) != 0;
            suspendedUntil = ParseNullableDate(reader, 4);
        }

        if (mutedUntil is not null && mutedUntil <= now)
        {
            isMuted = false;
        }

        if (suspendedUntil is not null && suspendedUntil <= now)
        {
            isSuspended = false;
        }

        return new CommunityUserContext(
            user,
            isAdmin,
            isAdmin || configuredModerator || databaseModerator,
            isMuted,
            isSuspended,
            mutedUntil,
            suspendedUntil);
    }

    private static async Task UpsertKnownUserAsync(
        SqliteConnection connection,
        Guid userId,
        string username,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO known_users(user_id, username, first_seen_utc, last_seen_utc, is_deleted)
            VALUES($userId, $username, $now, $now, 0)
            ON CONFLICT(user_id) DO UPDATE SET
                username = excluded.username,
                last_seen_utc = excluded.last_seen_utc,
                is_deleted = 0;
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$now", now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DateTime? ParseNullableDate(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return DateTime.TryParse(
            reader.GetString(ordinal),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var value) ? value : null;
    }
}
