using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Community.Infrastructure;

public sealed class CommunityPaths
{
    public CommunityPaths(IApplicationPaths applicationPaths)
        : this(Path.Combine(applicationPaths.DataPath, "community"))
    {
    }

    public CommunityPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        DatabasePath = Path.Combine(Root, "community.db");
        AttachmentsPath = Path.Combine(Root, "attachments");
        BackupsPath = Path.Combine(Root, "backups");
        TemporaryPath = Path.Combine(Root, "temp");
        PendingRestorePath = Path.Combine(Root, "restore-pending.zip");
    }

    public string Root { get; }

    public string DatabasePath { get; }

    public string AttachmentsPath { get; }

    public string BackupsPath { get; }

    public string TemporaryPath { get; }

    public string PendingRestorePath { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(AttachmentsPath);
        Directory.CreateDirectory(BackupsPath);
        Directory.CreateDirectory(TemporaryPath);
    }
}
