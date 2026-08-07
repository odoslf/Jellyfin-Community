using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Community.Domain;

public enum ThreadKind
{
    Discussion = 0,
    Review = 1,
    Poll = 2,
    Announcement = 3
}

public enum ReportState
{
    Open = 0,
    InReview = 1,
    Resolved = 2,
    Rejected = 3
}

public enum NotificationType
{
    Reply = 0,
    Mention = 1,
    Quote = 2,
    FollowedThread = 3,
    Moderation = 4,
    ReportResolved = 5,
    Announcement = 6
}

public sealed record CommunityUserDto(
    Guid Id,
    string Username,
    bool IsAdministrator,
    bool IsModerator,
    bool IsMuted,
    bool IsSuspended,
    DateTime? MutedUntilUtc,
    DateTime? SuspendedUntilUtc);

public sealed record CategoryDto(
    long Id,
    string Name,
    string Slug,
    string? Description,
    Guid? LibraryId,
    int SortOrder,
    bool IsReadOnly,
    bool RequiresApproval,
    bool IsArchived,
    long ThreadCount,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record ThreadSummaryDto(
    long Id,
    long CategoryId,
    string CategoryName,
    ThreadKind Kind,
    string Title,
    Guid AuthorUserId,
    string AuthorName,
    Guid? ItemId,
    string? ItemName,
    bool IsPinned,
    bool IsLocked,
    bool IsArchived,
    bool IsHidden,
    bool IsFollowing,
    int ReplyCount,
    int ViewCount,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime LastActivityUtc,
    IReadOnlyList<string> Tags);

public sealed record ThreadDto(
    ThreadSummaryDto Thread,
    PostDto FirstPost,
    PollDto? Poll,
    bool CanEdit,
    bool CanModerate);

public sealed record PostDto(
    long Id,
    long ThreadId,
    long? ParentPostId,
    Guid AuthorUserId,
    string AuthorName,
    string BodyMarkdown,
    string BodyHtml,
    bool ContainsSpoiler,
    Guid? SpoilerItemId,
    string? SpoilerLabel,
    bool SpoilerUnlocked,
    bool IsEdited,
    bool IsHidden,
    bool IsDeleted,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    IReadOnlyDictionary<string, int> Reactions,
    string? CurrentUserReaction,
    IReadOnlyList<AttachmentDto> Attachments,
    bool CanEdit,
    bool CanModerate);

public sealed record PollDto(
    long Id,
    long ThreadId,
    string Question,
    bool AllowMultiple,
    DateTime? ClosesUtc,
    IReadOnlyList<PollOptionDto> Options);

public sealed record PollOptionDto(long Id, string Text, int VoteCount, bool CurrentUserVoted);

public sealed record AttachmentDto(
    long Id,
    long PostId,
    string OriginalName,
    string MediaType,
    long SizeBytes,
    string Url,
    DateTime CreatedUtc);

public sealed record NotificationDto(
    long Id,
    NotificationType Type,
    string Title,
    string Message,
    long? ThreadId,
    long? PostId,
    bool IsRead,
    DateTime CreatedUtc);

public sealed record ReportDto(
    long Id,
    long PostId,
    long ThreadId,
    Guid ReporterUserId,
    string ReporterName,
    string Reason,
    string? Comment,
    ReportState State,
    Guid? AssignedModeratorUserId,
    string? Resolution,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record AuditEntryDto(
    long Id,
    Guid ActorUserId,
    string ActorName,
    string Action,
    string EntityType,
    string EntityId,
    string? Reason,
    string? BeforeJson,
    string? AfterJson,
    DateTime CreatedUtc);

public sealed record StorageStatsDto(
    long DatabaseBytes,
    long AttachmentsBytes,
    long BackupsBytes,
    int Categories,
    int Threads,
    int Posts,
    int Users,
    int OpenReports,
    DateTime GeneratedUtc);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total);

public sealed record CreateCategoryRequest(
    string Name,
    string? Description,
    Guid? LibraryId,
    int SortOrder,
    bool IsReadOnly,
    bool RequiresApproval);

public sealed record UpdateCategoryRequest(
    string Name,
    string? Description,
    Guid? LibraryId,
    int SortOrder,
    bool IsReadOnly,
    bool RequiresApproval,
    bool IsArchived);

public sealed record CreateThreadRequest(
    long CategoryId,
    ThreadKind Kind,
    string Title,
    string Body,
    Guid? ItemId,
    string? ItemName,
    IReadOnlyList<string>? Tags,
    bool ContainsSpoiler,
    Guid? SpoilerItemId,
    string? SpoilerLabel,
    CreatePollRequest? Poll);

public sealed record UpdateThreadRequest(
    string Title,
    IReadOnlyList<string>? Tags,
    bool ContainsSpoiler,
    Guid? SpoilerItemId,
    string? SpoilerLabel);

public sealed record CreatePostRequest(
    string Body,
    long? ParentPostId,
    bool ContainsSpoiler,
    Guid? SpoilerItemId,
    string? SpoilerLabel);

public sealed record UpdatePostRequest(
    string Body,
    bool ContainsSpoiler,
    Guid? SpoilerItemId,
    string? SpoilerLabel,
    string? EditReason);

public sealed record CreatePollRequest(
    string Question,
    bool AllowMultiple,
    DateTime? ClosesUtc,
    IReadOnlyList<string> Options);

public sealed record VotePollRequest(IReadOnlyList<long> OptionIds);

public sealed record ReactionRequest(string Reaction);

public sealed record ReportRequest(string Reason, string? Comment);

public sealed record ResolveReportRequest(ReportState State, string Resolution);

public sealed record ThreadStateRequest(
    bool? IsPinned,
    bool? IsLocked,
    bool? IsArchived,
    bool? IsHidden,
    long? MoveToCategoryId,
    string? Reason);

public sealed record UserStatusRequest(
    bool IsSuspended,
    DateTime? SuspendedUntilUtc,
    bool IsMuted,
    DateTime? MutedUntilUtc,
    string? Reason);

public sealed record NotificationReadRequest(IReadOnlyList<long> NotificationIds, bool MarkAll);

public sealed record DraftRequest(string Key, string Body, string? MetadataJson);

public sealed record DraftDto(string Key, string Body, string? MetadataJson, DateTime UpdatedUtc);

public sealed record SearchQuery(
    long? CategoryId,
    Guid? ItemId,
    string? Query,
    string Sort,
    int Page,
    int PageSize,
    bool FollowedOnly = false,
    bool UnreadOnly = false);

public sealed record ModerationQuery(ReportState? State, int Page, int PageSize);

public sealed record CommunityUserContext(
    Jellyfin.Data.Entities.User User,
    bool IsAdministrator,
    bool IsModerator,
    bool IsMuted,
    bool IsSuspended,
    DateTime? MutedUntilUtc,
    DateTime? SuspendedUntilUtc)
{
    [JsonIgnore]
    public Guid UserId => User.Id;

    [JsonIgnore]
    public string Username => User.Username;
}
