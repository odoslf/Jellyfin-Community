using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.Community.WebIntegration;

public sealed class CommunityWebInjectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly CommunityWebIntegrationState _state;
    private readonly ILogger<CommunityWebInjectionMiddleware> _logger;

    public CommunityWebInjectionMiddleware(
        RequestDelegate next,
        CommunityWebIntegrationState state,
        ILogger<CommunityWebInjectionMiddleware> logger)
    {
        _next = next;
        _state = state;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldInspect(context.Request.Path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        _state.RecordRequest();

        var originalBody = context.Response.Body;
        var originalAcceptEncoding = context.Request.Headers[HeaderNames.AcceptEncoding];
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        context.Request.Headers.Remove(HeaderNames.AcceptEncoding);

        try
        {
            await _next(context).ConfigureAwait(false);
            context.Request.Headers[HeaderNames.AcceptEncoding] = originalAcceptEncoding;
            context.Response.Body = originalBody;

            buffer.Position = 0;
            if (!ShouldTransform(context.Response))
            {
                await buffer.CopyToAsync(originalBody, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var html = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
            var version = typeof(Plugin).Assembly.GetName().Version ?? new Version(1, 1, 0, 0);
            var transformed = CommunityIndexHtmlTransformer.InjectBootstrap(html, version);
            var payload = Encoding.UTF8.GetBytes(transformed);

            context.Response.ContentLength = payload.LongLength;
            context.Response.Headers.Remove(HeaderNames.ContentEncoding);
            await originalBody.WriteAsync(payload, context.RequestAborted).ConfigureAwait(false);

            if (!string.Equals(transformed, html, StringComparison.Ordinal))
            {
                _state.RecordTransformed();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            _state.RecordError(exception);
            _logger.LogError(exception, "Community failed to inject its Jellyfin Web bootstrap script.");
            context.Response.Body = originalBody;

            if (buffer.CanSeek)
            {
                buffer.Position = 0;
                await buffer.CopyToAsync(originalBody, context.RequestAborted).ConfigureAwait(false);
            }
        }
        finally
        {
            context.Request.Headers[HeaderNames.AcceptEncoding] = originalAcceptEncoding;
            context.Response.Body = originalBody;
        }
    }

    private static bool ShouldInspect(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return value.EndsWith("/web", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/web/index.htm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldTransform(HttpResponse response)
    {
        if (response.StatusCode != StatusCodes.Status200OK)
        {
            return false;
        }

        return response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
    }
}
