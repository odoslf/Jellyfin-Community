using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Community.Services;

public sealed class BackupArchiveValidator
{
    public const string BackupFormat = "jellyfin-community-backup";
    public const int BackupFormatVersion = 1;
    // Backup-format compatibility guard for the Jellyfin 10.10 family; catalog compatibility is 10.10.7.0.
    public const string TargetAbi = "10.10.0.0";
    private const int MaximumEntries = 100_000;
    private const long MaximumManifestBytes = 64 * 1024;
    private const long MinimumRestoreLimit = 64L * 1024 * 1024;
    private const long MaximumRestoreLimit = 2L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 16
    };

    public static long GetMaximumArchiveBytes()
    {
        var configuration = Plugin.Instance?.Configuration;
        var attachmentQuota = Math.Max(0, configuration?.GlobalAttachmentQuotaBytes ?? 512L * 1024 * 1024);
        var databaseAllowance = Math.Max(0, configuration?.DatabaseCriticalBytes ?? 750L * 1024 * 1024);
        long requested;
        try
        {
            requested = checked(attachmentQuota + databaseAllowance + MaximumManifestBytes);
        }
        catch (OverflowException)
        {
            requested = MaximumRestoreLimit;
        }

        return Math.Clamp(requested, MinimumRestoreLimit, MaximumRestoreLimit);
    }

    public async Task<CommunityBackupManifest> ValidateAndExtractAsync(
        string archivePath,
        string destinationDirectory,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        maximumBytes = Math.Clamp(maximumBytes, MinimumRestoreLimit, MaximumRestoreLimit);

        var archiveInfo = new FileInfo(archivePath);
        if (!archiveInfo.Exists || archiveInfo.Length <= 0 || archiveInfo.Length > maximumBytes)
        {
            throw new CommunityValidationException("The restore archive size is invalid or exceeds the configured limit.");
        }

        if (Directory.Exists(destinationDirectory) && Directory.EnumerateFileSystemEntries(destinationDirectory).Any())
        {
            throw new InvalidOperationException("The backup validation destination must be empty.");
        }

        Directory.CreateDirectory(destinationDirectory);
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count is 0 or > MaximumEntries)
        {
            throw new CommunityValidationException("The restore archive contains an invalid number of entries.");
        }

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long totalLength = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = ValidateEntryName(entry);
            if (!entries.TryAdd(name, entry))
            {
                throw new CommunityValidationException("The restore archive contains duplicate paths.");
            }

            try
            {
                totalLength = checked(totalLength + entry.Length);
            }
            catch (OverflowException)
            {
                throw new CommunityValidationException("The restore archive expands beyond the configured limit.");
            }

            if (totalLength > maximumBytes)
            {
                throw new CommunityValidationException("The restore archive expands beyond the configured limit.");
            }
        }

        if (!entries.TryGetValue("manifest.json", out var manifestEntry)
            || !entries.TryGetValue("community.db", out var databaseEntry))
        {
            throw new CommunityValidationException("The restore archive is missing manifest.json or community.db.");
        }

        if (manifestEntry.Length <= 0 || manifestEntry.Length > MaximumManifestBytes || databaseEntry.Length <= 0)
        {
            throw new CommunityValidationException("The restore archive contains an invalid manifest or database.");
        }

        CommunityBackupManifest manifest;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<CommunityBackupManifest>(manifestStream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new CommunityValidationException("The restore manifest is empty.");
        }

        ValidateManifest(manifest);
        await VerifyDatabaseHashAsync(databaseEntry, manifest.DatabaseSha256, cancellationToken).ConfigureAwait(false);

        foreach (var pair in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = pair.Value;
            var outputPath = GetExtractionPath(destinationDirectory, pair.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await using var input = entry.Open();
            await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyExactAsync(input, output, entry.Length, cancellationToken).ConfigureAwait(false);
        }

        await VerifySqliteIntegrityAsync(Path.Combine(destinationDirectory, "community.db"), cancellationToken).ConfigureAwait(false);
        return manifest;
    }

    private static string ValidateEntryName(ZipArchiveEntry entry)
    {
        var name = entry.FullName;
        if (string.IsNullOrWhiteSpace(name)
            || name.Contains('\\')
            || name.StartsWith('/')
            || name.Contains(':')
            || name.EndsWith('/'))
        {
            throw new CommunityValidationException("The restore archive contains an unsafe or unsupported path.");
        }

        var segments = name.Split('/', StringSplitOptions.None);
        if (segments.Any(segment => string.IsNullOrEmpty(segment) || segment is "." or ".."))
        {
            throw new CommunityValidationException("The restore archive contains a traversal path.");
        }

        if (string.Equals(name, "manifest.json", StringComparison.Ordinal)
            || string.Equals(name, "community.db", StringComparison.Ordinal))
        {
            return name;
        }

        if (segments.Length == 2
            && string.Equals(segments[0], "attachments", StringComparison.Ordinal)
            && AttachmentService.IsSafeStoredName(segments[1]))
        {
            return "attachments/" + segments[1];
        }

        throw new CommunityValidationException("The restore archive contains an unexpected entry.");
    }

    private static void ValidateManifest(CommunityBackupManifest manifest)
    {
        if (!string.Equals(manifest.Format, BackupFormat, StringComparison.Ordinal)
            || manifest.FormatVersion != BackupFormatVersion
            || !string.Equals(manifest.TargetJellyfinAbi, TargetAbi, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(manifest.PluginVersion)
            || string.IsNullOrWhiteSpace(manifest.DatabaseSha256))
        {
            throw new CommunityValidationException("The restore manifest is incompatible with this Community version.");
        }

        if (manifest.DatabaseSha256.Length != 64 || !manifest.DatabaseSha256.All(Uri.IsHexDigit))
        {
            throw new CommunityValidationException("The restore manifest contains an invalid database checksum.");
        }
    }

    private static async Task VerifyDatabaseHashAsync(ZipArchiveEntry databaseEntry, string expectedHex, CancellationToken cancellationToken)
    {
        await using var stream = databaseEntry.Open();
        var actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedHex);
        }
        catch (FormatException)
        {
            throw new CommunityValidationException("The restore manifest contains an invalid database checksum.");
        }

        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new CommunityValidationException("The restore database checksum does not match the manifest.");
        }
    }

    private static string GetExtractionPath(string destinationDirectory, string entryName)
    {
        var root = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(destinationDirectory, entryName.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root, StringComparison.Ordinal))
        {
            throw new CommunityValidationException("The restore archive contains a path outside the staging directory.");
        }

        return candidate;
    }

    private static async Task CopyExactAsync(Stream input, Stream output, long expectedLength, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            copied = checked(copied + read);
            if (copied > expectedLength)
            {
                throw new CommunityValidationException("A restore archive entry expanded beyond its declared size.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        if (copied != expectedLength)
        {
            throw new CommunityValidationException("A restore archive entry was truncated.");
        }
    }

    private static async Task VerifySqliteIntegrityAsync(string databasePath, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var results = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(reader.GetString(0));
        }

        if (results.Count != 1 || !string.Equals(results[0], "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new CommunityValidationException("The restore database failed SQLite integrity_check.");
        }
    }
}

public sealed class CommunityBackupManifest
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; }

    [JsonPropertyName("pluginVersion")]
    public string PluginVersion { get; set; } = string.Empty;

    [JsonPropertyName("targetJellyfinAbi")]
    public string TargetJellyfinAbi { get; set; } = string.Empty;

    [JsonPropertyName("createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [JsonPropertyName("databaseSha256")]
    public string DatabaseSha256 { get; set; } = string.Empty;
}
