namespace Axumera.LicenseManager.Core.Models;

/// <summary>Outcome of verifying a license.lic file against the production public key.</summary>
public sealed class LicenseVerificationResult
{
    public bool Verified { get; init; }

    /// <summary>Top-level failure reason when <see cref="Verified"/> is false.</summary>
    public string? Error { get; init; }

    public string? SchoolName { get; init; }

    /// <summary>Normalized HWID carried by the signed payload.</summary>
    public string? HardwareId { get; init; }

    /// <summary>Expiry date (yyyy-MM-dd) carried by the signed payload.</summary>
    public string? Expires { get; init; }

    /// <summary>true when the signature is valid but the expiry date is past.</summary>
    public bool Expired { get; init; }

    /// <summary>Only populated when an expected machine HWID was supplied for comparison.</summary>
    public bool? MachineMatches { get; init; }

    public static LicenseVerificationResult Invalid(string error) => new() { Error = error };
}