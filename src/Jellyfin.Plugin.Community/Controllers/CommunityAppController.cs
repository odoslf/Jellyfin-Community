using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.Community.Controllers;

[ApiController]
[AllowAnonymous]
[Route("Community")]
public sealed class CommunityAppController : ControllerBase
{
    private static readonly byte[] AppHtml = ReadResource("Web.communityForum15.html");
    private static readonly byte[] AppCss = ReadResource("Web.communityForum15.css");
    private static readonly byte[] AppJavaScript = ReadResource("Web.communityForum15.js");
    private static readonly byte[] BootstrapJavaScript = ReadResource("Web.communityBootstrap.js");

    [HttpGet("app")]
    [HttpGet("app/")]
    public IActionResult GetApp()
    {
        SetNoCacheHeaders();
        Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; base-uri 'self'; connect-src 'self'; img-src 'self' data: blob: https: http:; "
            + "object-src 'none'; script-src 'self'; style-src 'self'; frame-ancestors 'self'";
        return File(AppHtml, "text/html; charset=utf-8");
    }

    [HttpGet("assets/communityForum15.css")]
    public IActionResult GetAppCss()
    {
        SetNoCacheHeaders();
        return File(AppCss, "text/css; charset=utf-8");
    }

    [HttpGet("assets/communityForum15.js")]
    public IActionResult GetAppJavaScript()
    {
        SetNoCacheHeaders();
        return File(AppJavaScript, "application/javascript; charset=utf-8");
    }

    [HttpGet("assets/communityBootstrap15.js")]
    public IActionResult GetBootstrapJavaScript()
    {
        SetNoCacheHeaders();
        return File(BootstrapJavaScript, "application/javascript; charset=utf-8");
    }

    private void SetNoCacheHeaders()
    {
        Response.Headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
        Response.Headers[HeaderNames.Pragma] = "no-cache";
        Response.Headers[HeaderNames.Expires] = "0";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static byte[] ReadResource(string suffix)
    {
        var assembly = typeof(CommunityAppController).Assembly;
        var resourceName = $"{typeof(Plugin).Namespace}.{suffix}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded Community resource '{resourceName}' was not found.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
