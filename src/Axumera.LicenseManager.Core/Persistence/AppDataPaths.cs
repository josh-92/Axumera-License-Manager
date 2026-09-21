namespace Axumera.LicenseManager.Core.Persistence;

/// <summary>
/// Well-known locations for the License Manager's local JSON store. Everything
/// lives under <c>%LOCALAPPDATA%\Axumera\LicenseManager</c>, which is created
/// with a restrictive DACL (current user, Administrators, SYSTEM only).
/// </summary>
public sealed class AppDataPaths
{
    public static AppDataPaths Default { get; } = new(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public AppDataPaths(string localAppDataRoot)
        => Root = Path.Combine(localAppDataRoot, "Axumera", "LicenseManager");

    public string Root { get; }

    public string AccountsFile => Path.Combine(Root, "accounts.json");

    public string LicensesFile => Path.Combine(Root, "licenses.json");

    public string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>Creates the data directory and applies restrictive ACLs.</summary>
    public void EnsureReady()
    {
        Directory.CreateDirectory(Root);
        try
        {
            Security.AclGuard.RestrictDirectory(Root);
        }
        catch (UnauthorizedAccessException)
        {
            // Non-fatal: the per-file ACL is applied to accounts.json regardless.
        }
    }
}