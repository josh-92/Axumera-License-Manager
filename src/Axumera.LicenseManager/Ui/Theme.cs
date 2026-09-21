using System.Drawing;
using Axumera.LicenseManager.Core.Accounts;
using Axumera.LicenseManager.Core.Persistence;

namespace Axumera.LicenseManager.Ui;

/// <summary>
/// Light theme for the License Manager, mirroring the official Axumera palette
/// (source: Axumera.Core.Branding.AxumeraBrand): Gold #D3A029, Deep Navy
/// #0C2036, Light Gray #F2F4F6, Border #D9DEE3, Muted #6B7280.
/// </summary>
internal static class Theme
{
    public static readonly Color Gold = Color.FromArgb(0xD3, 0xA0, 0x29);
    public static readonly Color DeepNavy = Color.FromArgb(0x0C, 0x20, 0x36);
    public static readonly Color White = Color.White;
    public static readonly Color LightGray = Color.FromArgb(0xF2, 0xF4, 0xF6);
    public static readonly Color BorderGray = Color.FromArgb(0xD9, 0xDE, 0xE3);
    public static readonly Color Muted = Color.FromArgb(0x6B, 0x72, 0x80);

    public static readonly Color Success = Color.FromArgb(0x1F, 0x7A, 0x3D);
    public static readonly Color Danger = Color.FromArgb(0xB3, 0x26, 0x1E);
    public static readonly Color Warn = Color.FromArgb(0xB8, 0x86, 0x0B);

    public static readonly Font TitleFont = new("Segoe UI", 15f, FontStyle.Bold);
    public static readonly Font SectionFont = new("Segoe UI", 10.5f, FontStyle.Bold);
    public static readonly Font BodyFont = new("Segoe UI", 9.5f);
    public static readonly Font CaptionFont = new("Segoe UI", 8.25f);

    public static Label Title(string text) => new()
    {
        Text = text,
        Font = TitleFont,
        ForeColor = DeepNavy,
        AutoSize = true,
    };

    public static Label Section(string text) => new()
    {
        Text = text,
        Font = SectionFont,
        ForeColor = DeepNavy,
        AutoSize = true,
    };

    public static Label Caption(string text) => new()
    {
        Text = text,
        Font = CaptionFont,
        ForeColor = Muted,
        AutoSize = true,
    };

    public static Label Body(string text) => new()
    {
        Text = text,
        Font = BodyFont,
        ForeColor = DeepNavy,
        AutoSize = true,
    };

    public static Button GoldButton(string text) => new()
    {
        Text = text,
        FlatStyle = FlatStyle.Flat,
        BackColor = Gold,
        ForeColor = White,
        Font = SectionFont,
        FlatAppearance = { BorderSize = 0, BorderColor = Gold },
        AutoSize = true,
        Padding = new Padding(16, 6, 16, 6),
        Cursor = Cursors.Hand,
    };

    public static Button SecondaryButton(string text) => new()
    {
        Text = text,
        FlatStyle = FlatStyle.Flat,
        BackColor = White,
        ForeColor = DeepNavy,
        Font = SectionFont,
        FlatAppearance = { BorderSize = 1, BorderColor = BorderGray },
        AutoSize = true,
        Padding = new Padding(16, 6, 16, 6),
        Cursor = Cursors.Hand,
    };

    public static Panel Card(int width, int height) => new()
    {
        Size = new Size(width, height),
        BackColor = White,
        Padding = new Padding(12),
        Margin = new Padding(0, 0, 12, 12),
    };

    public static TextBox TextBox(int width, int height = 30) => new()
    {
        Width = width,
        Height = height,
        Font = BodyFont,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = White,
    };

    public static Label StatusBadge(string text, Color color) => new()
    {
        Text = text,
        Font = SectionFont,
        ForeColor = color,
        AutoSize = true,
    };

    public static void Apply(Form form)
    {
        form.BackColor = White;
        form.ForeColor = DeepNavy;
    }
}

/// <summary>Shared app-wide services and the signed-in user, passed to views and dialogs.</summary>
internal sealed class AppSession
{
    public AppSession(AppDataPaths paths, AccountService accounts, JsonSettingsStore settings, JsonLicenseRecordStore licenses, string username, bool authenticated)
    {
        Paths = paths;
        Accounts = accounts;
        Settings = settings;
        Licenses = licenses;
        Username = username;
        Authenticated = authenticated;
    }

    public AppDataPaths Paths { get; }
    public AccountService Accounts { get; }
    public JsonSettingsStore Settings { get; }
    public JsonLicenseRecordStore Licenses { get; }
    public string Username { get; }

    /// <summary>
    /// True only after a verified credential check (login or first-run account
    /// creation). The shell refuses to serve any action unless this is set, so
    /// the dashboard can never be reached without real authentication.
    /// </summary>
    public bool Authenticated { get; }

    /// <summary>Raised when settings that affect other views change (key path, etc.).</summary>
    public event Action? SettingsChanged;

    public void NotifySettingsChanged() => SettingsChanged?.Invoke();
}