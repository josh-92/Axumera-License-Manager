namespace Axumera.LicenseManager.Core.Versioning;

/// <summary>
/// Identity constants for the AXUMERA LICENSE MANAGER desktop tool.
/// </summary>
public static class ProductInfo
{
    public const string ProductName = "AXUMERA LICENSE MANAGER";
    public const string ShortName = "Axumera License Manager";
    public const string Version = "1.0.0";
    public const string LicenseFormatVersion = "1.0";

    public const string Copyright = "© 2026 Axumera Technologies. All rights reserved.";

    public static string FullLabel => $"{ShortName} · Version {Version}";
}