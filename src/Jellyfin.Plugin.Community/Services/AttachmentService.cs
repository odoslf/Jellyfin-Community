using System.Security.Cryptography;
using Jellyfin.Plugin.Community.Domain;
using Jellyfin.Plugin.Community.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Community.Services;

public sealed class AttachmentService
{
    private readonly CommunityDatabase _database;
    private readonly CommunityPaths _paths;

    public AttachmentService(CommunityDatabase database, CommunityPaths paths)
    {
        _database = database;
        _paths = paths;
    }

    public async Task<AttachmentDto> UploadAsync(
        CommunityUserContext user,
        long postId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration?.EnableAttachments != true)
        {
            throw new CommunityForbiddenException("Attachments are disabled.");
        }

        if (file.Length <= 0 || file.Length > configuration.MaxAttachmentBytes)
        {
            throw new CommunityValidationException($"Attachment size must be between 1 and {configuration.MaxAttachmentBytes} bytes.");
        }

        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnsurePostOwnershipAsync(connection, user, postId, cancellationToken).ConfigureAwait(false);
        await EnsureAttachmentLimitsAsync(connection, postId, configuration.MaxAttachmentsPerPost, configuration.GlobalAttachmentQuotaBytes, file.Length, cancellationToken).ConfigureAwait(false);

        _paths.EnsureCreated();
        var temporary = Path.Combine(_paths.TemporaryPath, Guid.NewGuid().ToString("N") + ".upload");
        string mediaType;
        string extension;
        string sha256;
        try
        {
            await using (var source = file.OpenReadStream())
            await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            var info = new FileInfo(temporary);
            if (info.Length != file.Length || info.Length > configuration.MaxAttachmentBytes)
            {
                throw new CommunityValidationException("Attachment size changed while uploading or exceeds the configured maximum.");
            }

            (mediaType, extension) = await DetectImageTypeAsync(temporary, cancellationToken).ConfigureAwait(false);
            sha256 = await ComputeSha256Async(temporary, cancellationToken).ConfigureAwait(false);
            var storedName = Guid.NewGuid().ToString("N") + extension;
            var finalPath = Path.Combine(_paths.AttachmentsPath, storedName);
            File.Move(temporary, finalPath);

            try
            {
                var now = DateTime.UtcNow;
                var originalName = SanitizeOriginalName(file.FileName);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO attachments(post_id, uploader_user_id, original_name, stored_name, media_type, size_bytes, sha256, created_utc)
                    VALUES($postId, $userId, $originalName, $storedName, $mediaType, $size, $sha256, $created);
                    SELECT last_insert_rowid();
                    """;
                command.Parameters.AddWithValue("$postId", postId);
                command.Parameters.AddWithValue("$userId", user.UserId.ToString("D"));
                command.Parameters.AddWithValue("$originalName", originalName);
                command.Parameters.AddWithValue("$storedName", storedName);
                command.Parameters.AddWithValue("$mediaType", mediaType);
                command.Parameters.AddWithValue("$size", info.Length);
                command.Parameters.AddWithValue("$sha256", sha256);
                command.Parameters.AddWithValue("$created", Format(now));
                var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                return new AttachmentDto(id, postId, originalName, mediaType, info.Length, $"Community/api/v1/attachments/{id}", now);
            }
            catch
            {
                TryDeleteFile(finalPath);
                throw;
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public async Task<(string Path, string MediaType, string DownloadName)> ResolveAsync(
        CommunityUserContext user,
        long attachmentId,
        PermissionService permissions,
        CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.stored_name, a.media_type, a.original_name, t.item_id
            FROM attachments a
            JOIN posts p ON p.id = a.post_id
            JOIN threads t ON t.id = p.thread_id
            WHERE a.id = $id AND a.deleted_utc IS NULL AND p.is_deleted = 0 AND t.deleted_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$id", attachmentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new CommunityNotFoundException("Attachment not found.");
        }

        var itemId = reader.IsDBNull(3) ? (Guid?)null : Guid.Parse(reader.GetString(3));
        permissions.EnsureItemVisible(user, itemId);
        var storedName = reader.GetString(0);
        if (!IsSafeStoredName(storedName))
        {
            throw new CommunityNotFoundException("Attachment metadata is invalid.");
        }

        var path = Path.Combine(_paths.AttachmentsPath, storedName);
        if (!File.Exists(path))
        {
            throw new CommunityNotFoundException("Attachment file is missing.");
        }

        return (path, reader.GetString(1), reader.GetString(2));
    }

    public long GetAttachmentStorageBytes()
    {
        if (!Directory.Exists(_paths.AttachmentsPath))
        {
            return 0;
        }

        return Directory.EnumerateFiles(_paths.AttachmentsPath, "*", SearchOption.TopDirectoryOnly)
            .Sum(path => new FileInfo(path).Length);
    }

    public async Task<int> PurgeDeletedAsync(CancellationToken cancellationToken)
    {
        var retention = Math.Max(0, Plugin.Instance?.Configuration.DeletedAttachmentRetentionDays ?? 30);
        var cutoff = DateTime.UtcNow.AddDays(-retention);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var deleted = 0;
        var rows = new List<(long Id, string StoredName)>();
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = """
                SELECT a.id, a.stored_name
                FROM attachments a
                JOIN posts p ON p.id = a.post_id
                WHERE (a.deleted_utc IS NOT NULL AND a.deleted_utc < $cutoff)
                   OR (p.is_deleted = 1 AND p.deleted_utc < $cutoff);
                """;
            select.Parameters.AddWithValue("$cutoff", Format(cutoff));
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        foreach (var row in rows)
        {
            if (IsSafeStoredName(row.StoredName))
            {
                var path = Path.Combine(_paths.AttachmentsPath, row.StoredName);
                TryDeleteFile(path);
            }

            await using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM attachments WHERE id = $id;";
            delete.Parameters.AddWithValue("$id", row.Id);
            deleted += await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    private static async Task EnsurePostOwnershipAsync(SqliteConnection connection, CommunityUserContext user, long postId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT author_user_id, created_utc FROM posts WHERE id = $postId AND is_deleted = 0;";
        command.Parameters.AddWithValue("$postId", postId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new CommunityNotFoundException("Post not found.");
        }

        var author = Guid.Parse(reader.GetString(0));
        var created = DateTime.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        if (!PermissionService.CanEditPost(user, author, created))
        {
            throw new CommunityForbiddenException("The post cannot be modified by this user.");
        }
    }

    private static async Task EnsureAttachmentLimitsAsync(
        SqliteConnection connection,
        long postId,
        int maxPerPost,
        long globalQuota,
        long incomingBytes,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM attachments WHERE post_id = $postId AND deleted_utc IS NULL),
                (SELECT COALESCE(SUM(size_bytes), 0) FROM attachments WHERE deleted_utc IS NULL);
            """;
        command.Parameters.AddWithValue("$postId", postId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (reader.GetInt64(0) >= Math.Max(0, maxPerPost))
        {
            throw new CommunityValidationException("The post attachment limit has been reached.");
        }

        if (reader.GetInt64(1) + incomingBytes > Math.Max(0, globalQuota))
        {
            throw new CommunityValidationException("The global Community attachment quota has been reached.");
        }
    }

    private static async Task<(string MediaType, string Extension)> DetectImageTypeAsync(string path, CancellationToken cancellationToken)
    {
        var header = new byte[16];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, header.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken).ConfigureAwait(false);
        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ("image/jpeg", ".jpg");
        }

        if (read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return ("image/png", ".png");
        }

        if (read >= 12
            && header.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && header.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return ("image/webp", ".webp");
        }

        throw new CommunityValidationException("Only valid JPEG, PNG and WebP images are accepted.");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static bool IsSafeStoredName(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length is not (36 or 37))
        {
            return false;
        }

        var extensionLength = value.EndsWith(".jpg", StringComparison.Ordinal) || value.EndsWith(".png", StringComparison.Ordinal) ? 4
            : value.EndsWith(".webp", StringComparison.Ordinal) ? 5
            : 0;
        if (extensionLength == 0 || value.Length - extensionLength != 32)
        {
            return false;
        }

        return IsLowerHex(value.AsSpan(0, 32));
    }

    private static bool IsLowerHex(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    internal static string SanitizeOriginalName(string value)
    {
        var name = Path.GetFileName(value ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return "image";
        }

        name = string.Concat(name.Where(character => !char.IsControl(character))).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return "image";
        }

        return name.Length <= 180 ? name : name[..180];
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup. The database row is the authoritative record.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup. The database row is the authoritative record.
        }
    }

    private static string Format(DateTime value) => value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
