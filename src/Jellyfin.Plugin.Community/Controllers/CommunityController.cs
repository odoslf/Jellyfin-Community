using Jellyfin.Plugin.Community.Domain;
using Jellyfin.Plugin.Community.Services;
using Jellyfin.Plugin.Community.WebIntegration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Community.Controllers;

[ApiController]
[Authorize]
[Route("Community/api/v1")]
[TypeFilter(typeof(CommunityExceptionFilter))]
public sealed class CommunityController : ControllerBase
{
    private readonly CurrentUserService _currentUser;
    private readonly CommunityService _community;
    private readonly NotificationService _notifications;
    private readonly AttachmentService _attachments;
    private readonly PermissionService _permissions;
    private readonly CommunityWebIntegrationState _webIntegration;

    public CommunityController(
        CurrentUserService currentUser,
        CommunityService community,
        NotificationService notifications,
        AttachmentService attachments,
        PermissionService permissions,
        CommunityWebIntegrationState webIntegration)
    {
        _currentUser = currentUser;
        _community = community;
        _notifications = notifications;
        _attachments = attachments;
        _permissions = permissions;
        _webIntegration = webIntegration;
    }

    [HttpGet("me")]
    public async Task<ActionResult<CommunityUserDto>> GetMe(CancellationToken cancellationToken)
    {
        var user = await UserAsync(cancellationToken).ConfigureAwait(false);
        return new CommunityUserDto(user.UserId, user.Username, user.IsAdministrator, user.IsModerator, user.IsMuted, user.IsSuspended, user.MutedUntilUtc, user.SuspendedUntilUtc);
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetCategories(CancellationToken cancellationToken)
        => Ok(await _community.GetCategoriesAsync(await UserAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false));

    [HttpPost("categories")]
    public async Task<ActionResult<CategoryDto>> CreateCategory([FromBody] CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await _community.CreateCategoryAsync(await UserAsync(cancellationToken).ConfigureAwait(false), request, cancellationToken).ConfigureAwait(false);
        return CreatedAtAction(nameof(GetCategories), new { id = result.Id }, result);
    }

    [HttpPut("categories/{id:long}")]
    public async Task<ActionResult<CategoryDto>> UpdateCategory(long id, [FromBody] UpdateCategoryRequest request, CancellationToken cancellationToken)
        => Ok(await _community.UpdateCategoryAsync(await UserAsync(cancellationToken).ConfigureAwait(false), id, request, cancellationToken).ConfigureAwait(false));

    [HttpDelete("categories/{id:long}")]
    public async Task<IActionResult> DeleteCategory(long id, CancellationToken cancellationToken)
    {
        await _community.DeleteCategoryAsync(await UserAsync(cancellationToken).ConfigureAwait(false), id, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpGet("threads")]
    public async Task<ActionResult<PagedResult<ThreadSummaryDto>>> GetThreads(
        [FromQuery] long? categoryId,
        [FromQuery] Guid? itemId,
        [FromQuery(Name = "q")] string? query,
        [FromQuery] string sort = "activity",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] bool followedOnly = false,
        [FromQuery] bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        var request = new SearchQuery(categoryId, itemId, query, sort, page, pageSize, followedOnly, unreadOnly);
        return Ok(await _community.GetThreadsAsync(await UserAsync(cancellationToken).ConfigureAwait(false), request, cancellationToken).ConfigureAwait(false));
    }

    [HttpGet("threads/{threadId:long}")]
    public async Task<ActionResult<ThreadDto>> GetThread(long threadId, [FromQuery] bool incrementView = true, CancellationToken cancellationToken = default)
        => Ok(await _community.GetThreadAsync(await UserAsync(cancellationToken).ConfigureAwait(false), threadId, incrementView, cancellationToken).ConfigureAwait(false));

    [HttpPost("threads")]
    public async Task<ActionResult<ThreadDto>> CreateThread([FromBody] CreateThreadRequest request, CancellationToken cancellationToken)
    {
        var result = await _community.CreateThreadAsync(await UserAsync(cancellationToken).ConfigureAwait(false), request, cancellationToken).ConfigureAwait(false);
        return CreatedAtAction(nameof(GetThread), new { threadId = result.Thread.Id }, result);
    }

    [HttpPut("threads/{threadId:long}")]
    public async Task<ActionResult<ThreadDto>> UpdateThread(long threadId, [FromBody] UpdateThreadRequest request, CancellationToken cancellationToken)
        => Ok(await _community.UpdateThreadAsync(await UserAsync(cancellationToken).ConfigureAwait(false), threadId, request, cancellationToken).ConfigureAwait(false));

    [HttpDelete("threads/{threadId:long}")]
    public async Task<IActionResult> DeleteThread(long threadId, [FromQuery] string? reason, [FromQuery] bool permanent = false, CancellationToken cancellationToken = default)
    {
        await _community.DeleteThreadAsync(await UserAsync(cancellationToken).ConfigureAwait(false), threadId, reason, permanent, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPost("threads/{threadId:long}/follow")]
    public async Task<IActionResult> FollowThread(long threadId, CancellationToken cancellationToken)
    {
        await _community.SetFollowAsync(await UserAsync(cancellationToken).ConfigureAwait(false), threadId, true, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpDelete("threads/{threadId:long}/follow")]
    public async Task<IActionResult> UnfollowThread(long threadId, CancellationToken cancellationToken)
    {
        await _community.SetFollowAsync(await UserAsync(cancellationToken).ConfigureAwait(false), threadId, false, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPost("threads/{threadId:long}/read")]
    public async Task<IActionResult> MarkThreadRead(long threadId, [FromQuery] long? lastReadPostId, CancellationToken cancellationToken)
    {
        await _community.MarkThreadReadAsync(await UserAsync(cancellationToken).ConfigureAwait(false), threadId, lastReadPostId, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpGet("threads/{threadId:long}/posts")]
    public async Task<ActionResult<PagedResult<PostDto>>> GetPosts(long threadId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        => Ok(await _community.GetPostsAsync(await UserAsync(cancellationToken).ConfigureAwait(false), threadId, page, pageSize, cancellationToken).ConfigureAwait(false));

    [HttpPost("threads/{threadId:long}/posts")]
    public async Task<ActionResult<PostDto>> CreatePost(long threadId, [FromBody] CreatePostRequest request, CancellationToken cancellationToken)
    {
        var result = await _community.CreatePostAsync(await UserAsync(cancellationToken).ConfigureAwait(false), threadId, request, cancellationToken).ConfigureAwait(false);
        return Created($"Community/api/v1/threads/{threadId}/posts/{result.Id}", result);
    }

    [HttpPut("posts/{postId:long}")]
    public async Task<ActionResult<PostDto>> UpdatePost(long postId, [FromBody] UpdatePostRequest request, CancellationToken cancellationToken)
        => Ok(await _community.UpdatePostAsync(await UserAsync(cancellationToken).ConfigureAwait(false), postId, request, cancellationToken).ConfigureAwait(false));

    [HttpDelete("posts/{postId:long}")]
    public async Task<IActionResult> DeletePost(long postId, [FromQuery] string? reason, [FromQuery] bool permanent = false, CancellationToken cancellationToken = default)
    {
        await _community.DeletePostAsync(await UserAsync(cancellationToken).ConfigureAwait(false), postId, reason, permanent, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPut("posts/{postId:long}/reaction")]
    public async Task<IActionResult> SetReaction(long postId, [FromBody] ReactionRequest request, CancellationToken cancellationToken)
    {
        await _community.SetReactionAsync(await UserAsync(cancellationToken).ConfigureAwait(false), postId, request.Reaction, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpDelete("posts/{postId:long}/reaction")]
    public async Task<IActionResult> RemoveReaction(long postId, CancellationToken cancellationToken)
    {
        await _community.SetReactionAsync(await UserAsync(cancellationToken).ConfigureAwait(false), postId, null, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPost("posts/{postId:long}/report")]
    public async Task<ActionResult<object>> ReportPost(long postId, [FromBody] ReportRequest request, CancellationToken cancellationToken)
    {
        var id = await _community.ReportPostAsync(await UserAsync(cancellationToken).ConfigureAwait(false), postId, request, cancellationToken).ConfigureAwait(false);
        return Created(string.Empty, new { id });
    }

    [HttpPost("threads/{threadId:long}/poll/vote")]
    public async Task<ActionResult<PollDto>> Vote(long threadId, [FromBody] VotePollRequest request, CancellationToken cancellationToken)
        => Ok(await _community.VoteAsync(await UserAsync(cancellationToken).ConfigureAwait(false), threadId, request, cancellationToken).ConfigureAwait(false));

    [HttpGet("notifications")]
    public async Task<ActionResult<PagedResult<NotificationDto>>> GetNotifications([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var user = await UserAsync(cancellationToken).ConfigureAwait(false);
        return Ok(await _notifications.GetAsync(user.UserId, page, pageSize, cancellationToken).ConfigureAwait(false));
    }

    [HttpPost("notifications/read")]
    public async Task<IActionResult> MarkNotificationsRead([FromBody] NotificationReadRequest request, CancellationToken cancellationToken)
    {
        var user = await UserAsync(cancellationToken).ConfigureAwait(false);
        await _notifications.MarkReadAsync(user.UserId, request.NotificationIds, request.MarkAll, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPut("drafts")]
    public async Task<IActionResult> SaveDraft([FromBody] DraftRequest request, CancellationToken cancellationToken)
    {
        await _community.SaveDraftAsync(await UserAsync(cancellationToken).ConfigureAwait(false), request, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpGet("drafts/{key}")]
    public async Task<ActionResult<DraftDto>> GetDraft(string key, CancellationToken cancellationToken)
    {
        var draft = await _community.GetDraftAsync(await UserAsync(cancellationToken).ConfigureAwait(false), key, cancellationToken).ConfigureAwait(false);
        return draft is null ? NotFound() : Ok(draft);
    }

    [HttpDelete("drafts/{key}")]
    public async Task<IActionResult> DeleteDraft(string key, CancellationToken cancellationToken)
    {
        await _community.DeleteDraftAsync(await UserAsync(cancellationToken).ConfigureAwait(false), key, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPost("posts/{postId:long}/attachments")]
    [RequestSizeLimit(26 * 1024 * 1024)]
    public async Task<ActionResult<AttachmentDto>> UploadAttachment(long postId, IFormFile file, CancellationToken cancellationToken)
    {
        var result = await _attachments.UploadAsync(await UserAsync(cancellationToken).ConfigureAwait(false), postId, file, cancellationToken).ConfigureAwait(false);
        return Created(result.Url, result);
    }

    [HttpGet("attachments/{attachmentId:long}")]
    public async Task<IActionResult> GetAttachment(long attachmentId, CancellationToken cancellationToken)
    {
        var resolved = await _attachments.ResolveAsync(await UserAsync(cancellationToken).ConfigureAwait(false), attachmentId, _permissions, cancellationToken).ConfigureAwait(false);
        return PhysicalFile(resolved.Path, resolved.MediaType, resolved.DownloadName, enableRangeProcessing: true);
    }

    [HttpGet("moderation/reports")]
    public async Task<ActionResult<PagedResult<ReportDto>>> GetReports([FromQuery] ReportState? state, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        => Ok(await _community.GetReportsAsync(await UserAsync(cancellationToken).ConfigureAwait(false), new ModerationQuery(state, page, pageSize), cancellationToken).ConfigureAwait(false));

    [HttpPost("moderation/reports/{reportId:long}/resolve")]
    public async Task<IActionResult> ResolveReport(long reportId, [FromBody] ResolveReportRequest request, CancellationToken cancellationToken)
    {
        await _community.ResolveReportAsync(await UserAsync(cancellationToken).ConfigureAwait(false), reportId, request, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPost("moderation/threads/{threadId:long}")]
    public async Task<IActionResult> ModerateThread(long threadId, [FromBody] ThreadStateRequest request, CancellationToken cancellationToken)
    {
        await _community.ModerateThreadAsync(await UserAsync(cancellationToken).ConfigureAwait(false), threadId, request, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPut("moderation/users/{userId:guid}/status")]
    public async Task<IActionResult> SetUserStatus(Guid userId, [FromBody] UserStatusRequest request, CancellationToken cancellationToken)
    {
        await _community.SetUserStatusAsync(await UserAsync(cancellationToken).ConfigureAwait(false), userId, request, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPut("admin/moderators/{userId:guid}")]
    public async Task<IActionResult> SetModerator(Guid userId, [FromQuery] bool enabled, [FromQuery] long? categoryId, CancellationToken cancellationToken)
    {
        await _community.SetModeratorAsync(await UserAsync(cancellationToken).ConfigureAwait(false), userId, enabled, categoryId, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpGet("admin/users")]
    public async Task<ActionResult<IReadOnlyList<CommunityKnownUserDto>>> GetKnownUsers(CancellationToken cancellationToken)
        => Ok(await _community.GetKnownUsersAsync(await UserAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false));

    [HttpGet("admin/stats")]
    public async Task<ActionResult<StorageStatsDto>> GetStats(CancellationToken cancellationToken)
        => Ok(await _community.GetStatsAsync(await UserAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false));

    [HttpGet("admin/web-integration")]
    public async Task<ActionResult<WebIntegrationStatusDto>> GetWebIntegration(CancellationToken cancellationToken)
    {
        var user = await UserAsync(cancellationToken).ConfigureAwait(false);
        _permissions.EnsureAdministrator(user);
        var version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "1.1.0.0";
        return Ok(new WebIntegrationStatusDto(
            version,
            _webIntegration.IndexRequestsSeen,
            _webIntegration.IndexResponsesTransformed,
            _webIntegration.ConfigRequestsSeen,
            _webIntegration.ConfigResponsesTransformed,
            _webIntegration.LastInjectionUtc,
            _webIntegration.LastError));
    }

    [HttpGet("admin/audit")]
    public async Task<ActionResult<PagedResult<AuditEntryDto>>> GetAudit([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        => Ok(await _community.GetAuditAsync(await UserAsync(cancellationToken).ConfigureAwait(false), page, pageSize, cancellationToken).ConfigureAwait(false));

    [HttpPost("admin/maintenance/cleanup")]
    public async Task<ActionResult<object>> Cleanup(CancellationToken cancellationToken)
    {
        var user = await UserAsync(cancellationToken).ConfigureAwait(false);
        _permissions.EnsureAdministrator(user);
        var result = await _community.CleanupAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new { result.Notifications, result.Drafts, result.Attachments });
    }

    [HttpGet("admin/maintenance/integrity")]
    public async Task<ActionResult<object>> Integrity(CancellationToken cancellationToken)
        => Ok(new { result = await _community.IntegrityCheckAsync(await UserAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false) });

    [HttpPost("admin/backups")]
    public async Task<IActionResult> CreateBackup(CancellationToken cancellationToken)
    {
        var path = await _community.CreateBackupAsync(await UserAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        return PhysicalFile(path, "application/zip", Path.GetFileName(path), enableRangeProcessing: true);
    }

    [HttpPost("admin/restore")]
    [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> StageRestore(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        await _community.StageRestoreAsync(await UserAsync(cancellationToken).ConfigureAwait(false), stream, cancellationToken).ConfigureAwait(false);
        return Accepted(new { restartRequired = true });
    }

    [HttpPost("admin/users/{userId:guid}/anonymize")]
    public async Task<IActionResult> Anonymize(Guid userId, [FromQuery] bool deletePersonalData = true, CancellationToken cancellationToken = default)
    {
        await _community.AnonymizeUserAsync(await UserAsync(cancellationToken).ConfigureAwait(false), userId, deletePersonalData, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    private Task<CommunityUserContext> UserAsync(CancellationToken cancellationToken)
        => _currentUser.GetRequiredAsync(HttpContext, cancellationToken);
}
