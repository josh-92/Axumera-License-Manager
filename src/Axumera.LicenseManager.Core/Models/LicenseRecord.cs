namespace Axumera.LicenseManager.Core.Models;

/// <summary>
/// A local management record for a generated license. This is NOT the license —
/// the cryptographically signed artifact remains the <c>license.lic</c> file.
/// It never carries secret material (no private key, no signature bytes).
/// </summary>
public sealed class LicenseRecord
{
    public string Id { get; init; } = string.Empty;

    public string SchoolName { get; init; } = string.Empty;

    /// <summary>Normalized HWID exactly as it was embedded in the signed payload.</summary>
    public string HardwareId { get; init; } = string.Empty;

    /// <summary>Expiry date, yyyy-MM-dd, matching the signed payload.</summary>
    public string Expires { get; init; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>Absolute path of the saved license.lic (may be empty if only metadata).</summary>
    public string LicenseFilePath { get; init; } = string.Empty;

    public bool IsArchived { get; init; }

    public DateTimeOffset? ArchivedUtc { get; init; }

    public static LicenseRecord Create(string schoolName, string normalizedHwid, string expires, string? licenseFilePath = null) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SchoolName = schoolName,
            HardwareId = normalizedHwid,
            Expires = expires,
            CreatedUtc = DateTimeOffset.UtcNow,
            LicenseFilePath = licenseFilePath ?? string.Empty,
        };
}