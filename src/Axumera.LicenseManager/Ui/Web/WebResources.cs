using System.Reflection;
using Microsoft.Web.WebView2.Core;

namespace Axumera.LicenseManager.Ui.Web;

/// <summary>
/// Serves the WebView2 frontend entirely from embedded resources over a private
/// virtual host (<c>https://app.axumera</c>). Nothing is loaded from the
/// network: the SPA (html/css/js) and the white Axumera logo all ship inside
/// the assembly, so the EXE stays self-contained and works offline. The only
/// resources ever served are those enumerated below — no filesystem path is
/// ever exposed to the page.
/// </summary>
internal static class WebResources
{
    public const string VirtualHost = "https://app.axumera";

    private static readonly Assembly Asm = typeof(WebResources).Assembly;

    private static readonly Dictionary<string, (string ResourceName, string ContentType)> Map = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["/"] = ("Axumera.LicenseManager.Ui.Web.wwwroot.index.html", "text/html; charset=utf-8"),
        ["/index.html"] = ("Axumera.LicenseManager.Ui.Web.wwwroot.index.html", "text/html; charset=utf-8"),
        ["/app.css"] = ("Axumera.LicenseManager.Ui.Web.wwwroot.app.css", "text/css; charset=utf-8"),
        ["/app.js"] = ("Axumera.LicenseManager.Ui.Web.wwwroot.app.js", "text/javascript; charset=utf-8"),
        ["/logo_white.png"] = ("Axumera.LicenseManager.Resources.logo_white.png", "image/png"),
    };

    public static void Install(CoreWebView2 webView)
    {
        webView.AddWebResourceRequestedFilter(VirtualHost + "/*", CoreWebView2WebResourceContext.All);
        webView.WebResourceRequested += (_, e) =>
        {
            var uri = new Uri(e.Request.Uri);
            if (!Map.TryGetValue(uri.AbsolutePath, out var entry))
            {
                e.Response = webView.Environment.CreateWebResourceResponse(
                    null, 404, "Not Found", "Content-Type: text/plain");
                return;
            }

            using var stream = Asm.GetManifestResourceStream(entry.ResourceName);
            if (stream is null)
            {
                e.Response = webView.Environment.CreateWebResourceResponse(
                    null, 404, "Not Found", "Content-Type: text/plain");
                return;
            }

            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            e.Response = webView.Environment.CreateWebResourceResponse(
                ms, 200, "OK", $"Content-Type: {entry.ContentType}\r\nCache-Control: no-store");
        };
    }
}