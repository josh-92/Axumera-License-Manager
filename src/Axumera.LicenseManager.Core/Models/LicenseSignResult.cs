namespace Axumera.LicenseManager.Core.Models;

/// <summary>Outcome of the license signing operation.</summary>
public sealed class LicenseSignResult
{
    public bool Ok { get; init; }

    /// <summary>Human-safe failure message when <see cref="Ok"/> is false.</summary>
    public string? Error { get; init; }

    /// <summary>Exact signed payload JSON text.</summary>
    public string? PayloadText { get; init; }

    public string? PayloadBase64 { get; init; }

    public string? SignatureBase64 { get; init; }

    /// <summary>Full license.lic content (UTF-8 text).</summary>
    public string? LicenseText { get; init; }

    public static LicenseSignResult Success(string payloadText, string payloadB64, string signatureB64, string licenseText) =>
        new() { Ok = true, PayloadText = payloadText, PayloadBase64 = payloadB64, SignatureBase64 = signatureB64, LicenseText = licenseText };

    public static LicenseSignResult Failure(string error) => new() { Ok = false, Error = error };
}