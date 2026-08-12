using System.Security.Cryptography;
using System.Text;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.Community.WebIntegration;

/// <summary>
/// Adds the Forum entry to Jellyfin Web's official menu configuration and injects
/// a small Android WebView compatibility bootstrap into the physical index file.
/// Both resources are served here because Jellyfin's static-file middleware can use
/// SendFileAsync, bypassing response-body wrappers on real installations.
/// </summary>
public sealed partial class CommunityWebInjectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly CommunityWebIntegrationState _state;
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<CommunityWebInjectionMiddleware> _logger;
    private readonly object _cacheLock = new();
    private CachedResource? _cachedIndex;
    private CachedResource? _cachedConfig;

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
        var resourceKind = GetResourceKind(context.Request);
        if (resourceKind == WebResourceKind.None || Plugin.Instance?.Configuration.Enabled != true)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (resourceKind == WebResourceKind.Index)
        {
            _state.RecordIndexRequest();
        }
        else
        {
            _state.RecordConfigRequest();
        }

        try
        {
            var cached = resourceKind == WebResourceKind.Index
                ? GetOrCreateIndex()
                : GetOrCreateConfig();
            if (cached is null)
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = cached.ContentType;
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

            if (resourceKind == WebResourceKind.Index)
            {
                _state.RecordIndexTransformed();
            }
            else
            {
                _state.RecordConfigTransformed();
            }
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

    private CachedResource? GetOrCreateIndex()
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
        var version = typeof(Plugin).Assembly.GetName().Version ?? new Version(1, 5, 0, 0);

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
            cached = new CachedResource(
                payload,
                $"\"community-index-{digest[..24]}\"",
                "text/html; charset=utf-8",
                lastWriteUtc,
                version);
            _cachedIndex = cached;
            return cached;
        }
    }

    private CachedResource? GetOrCreateConfig()
    {
        var webPath = _applicationPaths.WebPath;
        if (string.IsNullOrWhiteSpace(webPath))
        {
            return null;
        }

        var configPath = Path.Combine(webPath, "config.json");
        if (!File.Exists(configPath))
        {
            return null;
        }

        var lastWriteUtc = File.GetLastWriteTimeUtc(configPath);
        var version = typeof(Plugin).Assembly.GetName().Version ?? new Version(1, 5, 0, 0);
        var cached = _cachedConfig;
        if (cached is not null
            && cached.SourceLastWriteUtc == lastWriteUtc
            && cached.PluginVersion == version)
        {
            return cached;
        }

        lock (_cacheLock)
        {
            cached = _cachedConfig;
            if (cached is not null
                && cached.SourceLastWriteUtc == lastWriteUtc
                && cached.PluginVersion == version)
            {
                return cached;
            }

            var json = File.ReadAllText(configPath, Encoding.UTF8);
            var transformed = CommunityWebConfigTransformer.AddForumMenuLink(json, version);
            var payload = Encoding.UTF8.GetBytes(transformed);
            var digest = Convert.ToHexString(SHA256.HashData(payload));
            cached = new CachedResource(
                payload,
                $"\"community-config-{digest[..24]}\"",
                "application/json; charset=utf-8",
                lastWriteUtc,
                version);
            _cachedConfig = cached;
            return cached;
        }
    }

    private static WebResourceKind GetResourceKind(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return WebResourceKind.None;
        }

        var value = request.Path.Value?.TrimEnd('/');
        if (string.IsNullOrEmpty(value))
        {
            return WebResourceKind.None;
        }

        if (value.EndsWith("/web/config.json", StringComparison.OrdinalIgnoreCase))
        {
            return WebResourceKind.Config;
        }

        return value.EndsWith("/web", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/web/index.htm", StringComparison.OrdinalIgnoreCase)
            ? WebResourceKind.Index
            : WebResourceKind.None;
    }

    [LoggerMessage(EventId = 1101, Level = LogLevel.Error, Message = "Community failed to integrate the Forum with Jellyfin Web.")]
    private static partial void LogInjectionFailure(ILogger logger, Exception exception);

    private sealed record CachedResource(
        byte[] Payload,
        string ETag,
        string ContentType,
        DateTime SourceLastWriteUtc,
        Version PluginVersion);

    private enum WebResourceKind
    {
        None,
        Index,
        Config
    }
}
