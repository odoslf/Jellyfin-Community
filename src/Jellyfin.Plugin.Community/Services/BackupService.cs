using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Jellyfin.Plugin.Community.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Community.Services;

public sealed class BackupService
{
    private readonly CommunityDatabase _database;
    private readonly CommunityPaths _paths;
    private readonly BackupArchiveValidator _validator;
    private readonly SemaphoreSlim _backupLock = new(1, 1);

    public BackupService(CommunityDatabase database, CommunityPaths paths, BackupArchiveValidator validator)
    {
        _database = database;
        _paths = paths;
        _validator = validator;
    }

    public async Task<string> CreateBackupAsync(CancellationToken cancellationToken)
    {
        await _backupLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _paths.EnsureCreated();
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
            var destination = Path.Combine(_paths.BackupsPath, $"community-{timestamp}.zip");
            var temporary = Path.Combine(_paths.TemporaryPath, Guid.NewGuid().ToString("N") + ".backup.tmp");
            var databaseSnapshot = Path.Combine(_paths.TemporaryPath, Guid.NewGuid().ToString("N") + ".snapshot.db");
            try
            {
                await CreateDatabaseSnapshotAsync(databaseSnapshot, cancellationToken).ConfigureAwait(false);
                await using var archiveStream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    await AddFileAsync(archive, databaseSnapshot, "community.db", cancellationToken).ConfigureAwait(false);
                    if (Directory.Exists(_paths.AttachmentsPath))
                    {
                        foreach (var file in Directory.EnumerateFiles(_paths.AttachmentsPath, "*", SearchOption.TopDirectoryOnly))
                        {
                            var storedName = Path.GetFileName(file);
                            if (!AttachmentService.IsSafeStoredName(storedName))
                            {
                                throw new InvalidDataException($"Unsafe attachment filename cannot be backed up: {storedName}");
                            }

                            await AddFileAsync(archive, file, "attachments/" + storedName, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    var manifest = new CommunityBackupManifest
                    {
                        Format = BackupArchiveValidator.BackupFormat,
                        FormatVersion = BackupArchiveValidator.BackupFormatVersion,
                        PluginVersion = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
                        TargetJellyfinAbi = BackupArchiveValidator.TargetAbi,
                        CreatedUtc = DateTime.UtcNow,
                        DatabaseSha256 = await ComputeSha256Async(databaseSnapshot, cancellationToken).ConfigureAwait(false)
                    };
                    var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await JsonSerializer.SerializeAsync(entryStream, manifest, cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                await archiveStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                File.Move(temporary, destination);
                EnforceRetention();
                return destination;
            }
            finally
            {
                TryDeleteFile(temporary);
                TryDeleteFile(databaseSnapshot);
            }
        }
        finally
        {
            _backupLock.Release();
        }
    }

    public async Task StageRestoreAsync(Stream archiveStream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        await _backupLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _paths.EnsureCreated();
            var temporary = Path.Combine(_paths.TemporaryPath, Guid.NewGuid().ToString("N") + ".restore.tmp");
            var validationDirectory = Path.Combine(_paths.TemporaryPath, "validate-" + Guid.NewGuid().ToString("N"));
            try
            {
                var maximumBytes = BackupArchiveValidator.GetMaximumArchiveBytes();
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await CopyWithLimitAsync(archiveStream, output, maximumBytes, cancellationToken).ConfigureAwait(false);
                }

                await _validator.ValidateAndExtractAsync(temporary, validationDirectory, maximumBytes, cancellationToken).ConfigureAwait(false);
                await _database.CheckpointAsync(cancellationToken).ConfigureAwait(false);
                File.Move(temporary, _paths.PendingRestorePath, overwrite: true);
            }
            finally
            {
                TryDeleteFile(temporary);
                TryDeleteDirectory(validationDirectory);
            }
        }
        finally
        {
            _backupLock.Release();
        }
    }

    public IReadOnlyList<FileInfo> ListBackups()
    {
        _paths.EnsureCreated();
        return Directory.EnumerateFiles(_paths.BackupsPath, "community-*.zip", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ToArray();
    }

    public long GetBackupStorageBytes() => ListBackups().Sum(file => file.Length);

    private void EnforceRetention()
    {
        var keep = Math.Max(1, Plugin.Instance?.Configuration.BackupRetentionCount ?? 7);
        foreach (var backup in ListBackups().Skip(keep))
        {
            backup.Delete();
        }
    }

    private async Task CreateDatabaseSnapshotAsync(string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };
        await using var destination = new SqliteConnection(builder.ToString());
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
        source.BackupDatabase(destination);
    }

    private static async Task AddFileAsync(ZipArchive archive, string sourcePath, string entryName, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = entry.Open();
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyWithLimitAsync(Stream input, Stream output, long maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total = checked(total + read);
            if (total > maximumBytes)
            {
                throw new CommunityValidationException("The restore archive exceeds the configured size limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        if (total == 0)
        {
            throw new CommunityValidationException("The restore archive is empty.");
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
            // Cleanup is best-effort; a unique temporary name prevents reuse.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup is best-effort; a unique temporary name prevents reuse.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Cleanup is best-effort; the directory contains only staged copies.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup is best-effort; the directory contains only staged copies.
        }
    }
}
