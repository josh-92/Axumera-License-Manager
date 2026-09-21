using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Axumera.LicenseManager.Ui;

/// <summary>Loads the embedded branding assets (logo.png, app.ico).</summary>
internal static class Resources
{
    private static readonly Assembly Asm = typeof(Resources).Assembly;

    public static Image Logo { get; } = LoadEmbedded("logo.png", static ms => Image.FromStream(ms));

    public static Icon AppIcon { get; } = LoadEmbedded("app.ico", static ms => new Icon(ms));

    private static T LoadEmbedded<T>(string resourceName, Func<System.IO.Stream, T> loader)
        where T : class
    {
        using var stream = Asm.GetManifestResourceStream($"Axumera.LicenseManager.Resources.{resourceName}")
            ?? throw new InvalidOperationException($"Embedded resource 'Resources/{resourceName}' not found.");
        return loader(stream);
    }
}