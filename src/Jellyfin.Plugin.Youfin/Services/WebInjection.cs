using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Jellyfin.Plugin.Youfin.Services;

/// <summary>Pure helpers for adding the Youfin script to jellyfin-web's index.html.</summary>
public static class WebInjection
{
    public const string Marker = "data-youfin";

    /// <summary>True for the pages that serve jellyfin-web's index.html.</summary>
    public static bool IsIndexPath(PathString path) =>
        path.Equals("/web", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/web/", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/web/index.html", StringComparison.OrdinalIgnoreCase);

    /// <summary>Inserts the script tag before the last &lt;/head&gt;. Returns the html unchanged if there is no head or it is already there.</summary>
    public static string Inject(string html, string scriptUrl)
    {
        if (html.Contains(Marker, StringComparison.Ordinal))
        {
            return html;
        }

        var i = html.LastIndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return i < 0 ? html : html.Insert(i, $"<script {Marker} src=\"{scriptUrl}\" defer></script>");
    }
}

/// <summary>
/// Adds a small script to jellyfin-web so the Youfin view can appear inside the chosen Jellyfin libraries.
/// Only active when libraries are chosen and the option is on; otherwise jellyfin-web is served untouched.
/// </summary>
public sealed class WebInjectionMiddleware
{
    private readonly RequestDelegate _next;

    public WebInjectionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var cfg = Plugin.Instance?.Configuration;
        if (cfg is null || !cfg.ReplaceLibraryView || LibraryScanner.IncludedLibraries().Length == 0
            || !HttpMethods.IsGet(ctx.Request.Method) || !WebInjection.IsIndexPath(ctx.Request.Path))
        {
            await _next(ctx);
            return;
        }

        // Compression would hide the html from us, so ask downstream for plain content (index.html is small).
        ctx.Request.Headers.Remove("Accept-Encoding");
        var original = ctx.Features.Get<IHttpResponseBodyFeature>()!;
        var buffer = new MemoryStream();
        ctx.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(buffer));
        try
        {
            await _next(ctx);
        }
        finally
        {
            ctx.Features.Set(original);
        }

        var ok = ctx.Response.StatusCode == StatusCodes.Status200OK
                 && (ctx.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) ?? false)
                 && buffer.Length > 0
                 && string.IsNullOrEmpty(ctx.Response.Headers.ContentEncoding.ToString());
        byte[] body = buffer.ToArray();
        if (ok)
        {
            var html = Encoding.UTF8.GetString(body);
            body = Encoding.UTF8.GetBytes(WebInjection.Inject(html, ctx.Request.PathBase + "/Youfin/Web/inject.js?v=" + Plugin.Instance!.Version));
            // the body changed, so a validator of the original file would be wrong
            ctx.Response.Headers.Remove("ETag");
            ctx.Response.Headers.Remove("Last-Modified");
        }

        if (body.Length > 0)
        {
            ctx.Response.ContentLength = body.Length;
            await original.Stream.WriteAsync(body);
        }
    }
}

public sealed class YoufinStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<WebInjectionMiddleware>();
        next(app);
    };
}
