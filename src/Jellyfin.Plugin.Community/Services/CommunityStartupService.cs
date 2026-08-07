using Jellyfin.Plugin.Community.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Community.Services;

public sealed class CommunityStartupService : IHostedService
{
    private readonly CommunityDatabase _database;
    private readonly CommunityPaths _paths;
    private readonly BackupArchiveValidator _validator;
    private readonly ILogger<CommunityStartupService> _logger;

    public CommunityStartupService(
        CommunityDatabase database,
        CommunityPaths paths,
        BackupArchiveValidator validator,
        ILogger<CommunityStartupService> logger)
    {
        _database = database;
        _paths = paths;
        _validator = validator;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _paths.EnsureCreated();
        await ApplyPendingRestoreAsync(cancellationToken).ConfigureAwait(false);
        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Jellyfin Community initialized at {DatabasePath}", _paths.DatabasePath);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ApplyPendingRestoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.PendingRestorePath))
        {
            return;
        }

        var suffix = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
        var staging = Path.Combine(_paths.TemporaryPath, "restore-" + Guid.NewGuid().ToString("N"));
        var replacementAttachments = Path.Combine(_paths.Root, "attachments.restore-" + Guid.NewGuid().ToString("N"));
        var previousDatabase = _paths.DatabasePath + ".pre-restore-" + suffix;
        var previousAttachments = Path.Combine(_paths.Root, "attachments.pre-restore-" + suffix);
        var databaseTemporary = _paths.DatabasePath + ".restore.tmp";
        var hadDatabase = File.Exists(_paths.DatabasePath);
        var attachmentsMoved = false;
        var attachmentsReplaced = false;
        var databaseReplaced = false;

        try
        {
            await _validator.ValidateAndExtractAsync(
                _paths.PendingRestorePath,
                staging,
                BackupArchiveValidator.GetMaximumArchiveBytes(),
                cancellationToken).ConfigureAwait(false);

            if (hadDatabase)
            {
                File.Copy(_paths.DatabasePath, previousDatabase, overwrite: false);
            }

            TryDeleteFile(databaseTemporary);
            File.Copy(Path.Combine(staging, "community.db"), databaseTemporary, overwrite: false);

            Directory.CreateDirectory(replacementAttachments);
            var stagedAttachments = Path.Combine(staging, "attachments");
            if (Directory.Exists(stagedAttachments))
            {
                foreach (var source in Directory.EnumerateFiles(stagedAttachments, "*", SearchOption.TopDirectoryOnly))
                {
                    var storedName = Path.GetFileName(source);
                    if (!AttachmentService.IsSafeStoredName(storedName))
                    {
                        throw new CommunityValidationException("The validated restore contains an unsafe attachment name.");
                    }

                    File.Copy(source, Path.Combine(replacementAttachments, storedName), overwrite: false);
                }
            }

            TryDeleteFile(_paths.DatabasePath + "-wal");
            TryDeleteFile(_paths.DatabasePath + "-shm");
            File.Move(databaseTemporary, _paths.DatabasePath, overwrite: true);
            databaseReplaced = true;

            if (Directory.Exists(_paths.AttachmentsPath))
            {
                Directory.Move(_paths.AttachmentsPath, previousAttachments);
                attachmentsMoved = true;
            }

            Directory.Move(replacementAttachments, _paths.AttachmentsPath);
            attachmentsReplaced = true;
            File.Delete(_paths.PendingRestorePath);
            _logger.LogWarning(
                "A staged Community restore was applied. Previous database: {DatabaseBackup}; previous attachments: {AttachmentBackup}",
                hadDatabase ? previousDatabase : "none",
                attachmentsMoved ? previousAttachments : "none");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The staged Community restore failed and was not accepted.");
            TryDeleteFile(databaseTemporary);

            if (attachmentsReplaced)
            {
                TryDeleteDirectory(_paths.AttachmentsPath);
            }

            if (attachmentsMoved && Directory.Exists(previousAttachments))
            {
                Directory.Move(previousAttachments, _paths.AttachmentsPath);
            }

            if (databaseReplaced)
            {
                TryDeleteFile(_paths.DatabasePath);
                if (hadDatabase && File.Exists(previousDatabase))
                {
                    File.Copy(previousDatabase, _paths.DatabasePath, overwrite: false);
                }
            }
        }
        finally
        {
            TryDeleteDirectory(staging);
            TryDeleteDirectory(replacementAttachments);
        }
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
            // Startup recovery preserves the pending archive for a later retry.
        }
        catch (UnauthorizedAccessException)
        {
            // Startup recovery preserves the pending archive for a later retry.
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
            // Startup recovery preserves the pending archive for a later retry.
        }
        catch (UnauthorizedAccessException)
        {
            // Startup recovery preserves the pending archive for a later retry.
        }
    }
}
