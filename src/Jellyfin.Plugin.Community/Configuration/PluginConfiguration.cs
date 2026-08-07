using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Community.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;

    public bool EnableAttachments { get; set; }

    public long MaxAttachmentBytes { get; set; } = 2 * 1024 * 1024;

    public int MaxAttachmentsPerPost { get; set; } = 3;

    public long GlobalAttachmentQuotaBytes { get; set; } = 512L * 1024 * 1024;

    public int MaxTitleLength { get; set; } = 200;

    public int MaxPostLength { get; set; } = 20_000;

    public int EditWindowMinutes { get; set; } = 60;

    public int NewThreadsPerHour { get; set; } = 5;

    public int RepliesPerHour { get; set; } = 30;

    public int MinimumSecondsBetweenPosts { get; set; } = 10;

    public int MaxMentionsPerPost { get; set; } = 10;

    public int NotificationRetentionDays { get; set; } = 90;

    public int DraftRetentionDays { get; set; } = 30;

    public int DeletedAttachmentRetentionDays { get; set; } = 30;

    public int BackupRetentionCount { get; set; } = 7;

    public long DatabaseWarningBytes { get; set; } = 250L * 1024 * 1024;

    public long DatabaseCriticalBytes { get; set; } = 750L * 1024 * 1024;

    public string[] ModeratorUserIds { get; set; } = [];

    public string[] BlockedTerms { get; set; } = [];

    public string[] AllowedReactions { get; set; } = ["like", "love", "laugh", "insightful"];

    public bool RequireApprovalForFirstPost { get; set; }

    public int NewUserApprovalDays { get; set; } = 7;

    public bool EnableSmartSpoilers { get; set; } = true;

    public bool AllowRemoteImages { get; set; }

    public bool LogModerationActions { get; set; } = true;

    public int DefaultPageSize { get; set; } = 25;

    public int MaximumPageSize { get; set; } = 100;
}
