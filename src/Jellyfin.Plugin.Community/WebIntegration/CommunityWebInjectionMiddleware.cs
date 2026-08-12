using System.Security.Cryptography;
using System.Text;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.Community.WebIntegration;

/// <summary>
/// Injects the Community bootstrap into Jellyfin Web's physical index file.
/// Serving the physical file here is intentional: Jellyfin's static-file middleware
/// can use SendFileAsync, which may bypass a response-body stream wrapper and made
/// the 1.2 post-processing approach unreliable on real installations/reverse proxies.
/// </summary>
public sealed partial class CommunityWebInjectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly CommunityWebIntegrationState _state;
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<CommunityWebInjectionMiddleware> _logger;
    private readonly object _cacheLock = new();
    private CachedIndex? _cachedIndex;

    public CommunityWebInjectionMiddleware(
        RequestDelegate next,
        CommunityWebIntegrationState state,
        IApplicationPaths applicationPaths,
        ILogger<CommunityWebInjectionMiddleware> logger)
    {
        _next = next;
        _state = state;
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsIndexRequest(context.Request)
            || Plugin.Instance?.Configuration.Enabled != true)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        _state.RecordRequest();

        try
        {
            var cached = GetOrCreateIndex();
            if (cached is null)
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
            context.Response.Headers[HeaderNames.Pragma] = "no-cache";
            context.Response.Headers[HeaderNames.Expires] = "0";
            context.Response.Headers[HeaderNames.ETag] = cached.ETag;

            if (context.Request.Headers[HeaderNames.IfNoneMatch].Any(value =>
                    string.Equals(value, cached.ETag, StringComparison.Ordinal)))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            context.Response.ContentLength = cached.Payload.LongLength;
            if (!HttpMethods.IsHead(context.Request.Method))
            {
                await context.Response.Body.WriteAsync(cached.Payload, context.RequestAborted).ConfigureAwait(false);
            }

            _state.RecordTransformed();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            _state.RecordError(exception);
            LogInjectionFailure(_logger, exception);
            if (!context.Response.HasStarted)
            {
                await _next(context).ConfigureAwait(false);
            }
        }
    }

    private CachedIndex? GetOrCreateIndex()
    {
        var webPath = _applicationPaths.WebPath;
        if (string.IsNullOrWhiteSpace(webPath))
        {
            return null;
        }

        var indexPath = Path.Combine(webPath, "index.html");
        if (!File.Exists(indexPath))
        {
            return null;
        }

        var lastWriteUtc = File.GetLastWriteTimeUtc(indexPath);
        var version = typeof(Plugin).Assembly.GetName().Version ?? new Version(1, 4, 0, 0);

        var cached = _cachedIndex;
        if (cached is not null
            && cached.SourceLastWriteUtc == lastWriteUtc
            && cached.PluginVersion == version)
        {
            return cached;
        }

        lock (_cacheLock)
        {
            cached = _cachedIndex;
            if (cached is not null
                && cached.SourceLastWriteUtc == lastWriteUtc
                && cached.PluginVersion == version)
            {
                return cached;
            }

            var html = File.ReadAllText(indexPath, Encoding.UTF8);
            var transformed = CommunityIndexHtmlTransformer.InjectBootstrap(html, version);
            if (string.Equals(html, transformed, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Jellyfin Web index.html no contiene un cierre </body> donde inyectar Community.");
            }

            var payload = Encoding.UTF8.GetBytes(transformed);
            var digest = Convert.ToHexString(SHA256.HashData(payload));
            cached = new CachedIndex(payload, $"\"community-{digest[..24]}\"", lastWriteUtc, version);
            _cachedIndex = cached;
            return cached;
        }
    }

    private static bool IsIndexRequest(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        var value = request.Path.Value?.TrimEnd('/');
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return value.EndsWith("/web", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/web/index.htm", StringComparison.OrdinalIgnoreCase);
    }

    [LoggerMessage(EventId = 1101, Level = LogLevel.Error, Message = "Community failed to inject its Jellyfin Web bootstrap script.")]
    private static partial void LogInjectionFailure(ILogger logger, Exception exception);

    private sealed record CachedIndex(
        byte[] Payload,
        string ETag,
        DateTime SourceLastWriteUtc,
        Version PluginVersion);
}
