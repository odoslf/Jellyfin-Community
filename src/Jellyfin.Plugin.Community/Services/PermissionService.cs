using Jellyfin.Plugin.Community.Domain;
using Jellyfin.Plugin.Community.Infrastructure;
using MediaBrowser.Controller.Library;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Community.Services;

public sealed class PermissionService
{
    private readonly CommunityDatabase _database;
    private readonly ILibraryManager _libraryManager;

    public PermissionService(CommunityDatabase database, ILibraryManager libraryManager)
    {
        _database = database;
        _libraryManager = libraryManager;
    }

    public void EnsureCanRead(CommunityUserContext user)
    {
        if (user.IsSuspended)
        {
            throw new CommunityForbiddenException("The user is suspended from Community.");
        }
    }

    public void EnsureCanWrite(CommunityUserContext user)
    {
        EnsureCanRead(user);
        if (user.IsMuted)
        {
            throw new CommunityForbiddenException("The user is muted in Community.");
        }
    }

    public void EnsureModerator(CommunityUserContext user)
    {
        EnsureCanRead(user);
        if (!user.IsModerator)
        {
            throw new CommunityForbiddenException("Moderator permission is required.");
        }
    }

    public void EnsureAdministrator(CommunityUserContext user)
    {
        EnsureCanRead(user);
        if (!user.IsAdministrator)
        {
            throw new CommunityForbiddenException("Jellyfin administrator permission is required.");
        }
    }

    public void EnsureItemVisible(CommunityUserContext user, Guid? itemId)
    {
        if (itemId is null)
        {
            return;
        }

        var item = _libraryManager.GetItemById(itemId.Value);
        if (item is null || !item.IsVisible(user.User))
        {
            throw new CommunityForbiddenException("The linked Jellyfin item is not available to this user.");
        }
    }

    public async Task EnsureCategoryVisibleAsync(CommunityUserContext user, long categoryId, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT library_id, is_archived FROM categories WHERE id = $id;";
        command.Parameters.AddWithValue("$id", categoryId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new CommunityNotFoundException("Category not found.");
        }

        if (reader.GetInt64(1) != 0 && !user.IsModerator)
        {
            throw new CommunityForbiddenException("The category is archived.");
        }

        if (!reader.IsDBNull(0) && Guid.TryParse(reader.GetString(0), out var libraryId))
        {
            EnsureItemVisible(user, libraryId);
        }
    }

    public static bool CanEditPost(CommunityUserContext user, Guid authorId, DateTime createdUtc)
    {
        if (user.IsModerator)
        {
            return true;
        }

        var editWindow = TimeSpan.FromMinutes(Math.Max(0, Plugin.Instance?.Configuration.EditWindowMinutes ?? 60));
        return user.UserId == authorId && DateTime.UtcNow - createdUtc <= editWindow;
    }
}

public sealed class CommunityForbiddenException : Exception
{
    public CommunityForbiddenException(string message)
        : base(message)
    {
    }
}

public sealed class CommunityNotFoundException : Exception
{
    public CommunityNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class CommunityValidationException : Exception
{
    public CommunityValidationException(string message)
        : base(message)
    {
    }
}
