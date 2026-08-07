using Jellyfin.Plugin.Community.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Community.Controllers;

public sealed class CommunityExceptionFilter : IExceptionFilter
{
    private readonly ILogger<CommunityExceptionFilter> _logger;

    public CommunityExceptionFilter(ILogger<CommunityExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        var (status, code) = context.Exception switch
        {
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "unauthorized"),
            CommunityForbiddenException => (StatusCodes.Status403Forbidden, "forbidden"),
            CommunityNotFoundException => (StatusCodes.Status404NotFound, "not_found"),
            CommunityValidationException => (StatusCodes.Status400BadRequest, "validation_error"),
            CommunityRateLimitException => (StatusCodes.Status429TooManyRequests, "rate_limited"),
            _ => (StatusCodes.Status500InternalServerError, "server_error")
        };

        if (status >= 500)
        {
            _logger.LogError(context.Exception, "Unhandled Jellyfin Community API error.");
        }

        context.Result = new ObjectResult(new
        {
            error = code,
            message = status >= 500 ? "An internal Community error occurred." : context.Exception.Message
        })
        {
            StatusCode = status
        };
        context.ExceptionHandled = true;
    }
}
