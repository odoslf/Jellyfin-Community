using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Jellyfin.Plugin.Community.Services;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Community.Tests;

public sealed class BackupArchiveValidatorTests
{
    [Fact]
    public async Task ValidateAndExtractAcceptsValidBackup()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var archivePath = await CreateArchiveAsync(root, BackupArchiveValidator.TargetAbi, validHash: true, extraEntry: null);
            var extraction = Path.Combine(root, "extract");

            var manifest = await new BackupArchiveValidator().ValidateAndExtractAsync(
                archivePath,
                extraction,
                64L * 1024 * 1024,
                CancellationToken.None);

            Assert.Equal(BackupArchiveValidator.TargetAbi, manifest.TargetJellyfinAbi);
            Assert.True(File.Exists(Path.Combine(extraction, "community.db")));
            Assert.True(File.Exists(Path.Combine(extraction, "manifest.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAndExtractRejectsTraversalEntry()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var archivePath = await CreateArchiveAsync(root, BackupArchiveValidator.TargetAbi, validHash: true, extraEntry: "../outside.txt");

            await Assert.ThrowsAsync<CommunityValidationException>(() => new BackupArchiveValidator().ValidateAndExtractAsync(
                archivePath,
                Path.Combine(root, "extract"),
                64L * 1024 * 1024,
                CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAndExtractRejectsWrongAbi()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var archivePath = await CreateArchiveAsync(root, "99.0.0.0", validHash: true, extraEntry: null);

            await Assert.ThrowsAsync<CommunityValidationException>(() => new BackupArchiveValidator().ValidateAndExtractAsync(
                archivePath,
                Path.Combine(root, "extract"),
                64L * 1024 * 1024,
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAndExtractRejectsDatabaseHashMismatch()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var archivePath = await CreateArchiveAsync(root, BackupArchiveValidator.TargetAbi, validHash: false, extraEntry: null);

            await Assert.ThrowsAsync<CommunityValidationException>(() => new BackupArchiveValidator().ValidateAndExtractAsync(
                archivePath,
                Path.Combine(root, "extract"),
                64L * 1024 * 1024,
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "community-backup-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> CreateArchiveAsync(string root, string targetAbi, bool validHash, string? extraEntry)
    {
        var databasePath = Path.Combine(root, "community.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE validation(id INTEGER PRIMARY KEY, value TEXT NOT NULL); INSERT INTO validation(value) VALUES('ok');";
            await command.ExecuteNonQueryAsync();
        }

        var hash = SHA256.HashData(await File.ReadAllBytesAsync(databasePath));
        var manifest = new CommunityBackupManifest
        {
            Format = BackupArchiveValidator.BackupFormat,
            FormatVersion = BackupArchiveValidator.BackupFormatVersion,
            PluginVersion = "1.0.0.0",
            TargetJellyfinAbi = targetAbi,
            CreatedUtc = DateTime.UtcNow,
            DatabaseSha256 = validHash ? Convert.ToHexString(hash).ToLowerInvariant() : new string('0', 64)
        };

        var archivePath = Path.Combine(root, "backup.zip");
        await using var archiveStream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous);
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var databaseEntry = archive.CreateEntry("community.db");
            await using (var output = databaseEntry.Open())
            await using (var input = File.OpenRead(databasePath))
            {
                await input.CopyToAsync(output);
            }

            var manifestEntry = archive.CreateEntry("manifest.json");
            await using (var output = manifestEntry.Open())
            {
                await JsonSerializer.SerializeAsync(output, manifest);
            }

            if (extraEntry is not null)
            {
                var entry = archive.CreateEntry(extraEntry);
                await using var writer = new StreamWriter(entry.Open());
                await writer.WriteAsync("blocked");
            }
        }

        await archiveStream.FlushAsync();
        return archivePath;
    }
}
