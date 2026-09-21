using System.Security.Cryptography;
using System.Text;
using Axumera.LicenseManager.Core.Models;
using Axumera.LicenseManager.Core.Rules;
using Axumera.LicenseManager.Core.Security;

namespace Axumera.LicenseManager.Core.Crypto;

/// <summary>
/// Signs licenses with the live production private key, byte-compatible with
/// the PHP generator. The key is loaded only for the duration of the signing
/// call; every other lifecycle concern (path validation, ACL checks) happens
/// before the key is read.
/// </summary>
public sealed class LicenseSigner
{
    private readonly PrivateKeyAccess _keyAccess;
    private readonly RSA? _verificationKey;

    /// <summary>
    /// <paramref name="verificationKey"/> overrides the key used for the post-sign
    /// compatibility gate. Production callers leave it null (embedded production
    /// public key); tests pass their ephemeral public key.
    /// </summary>
    public LicenseSigner(PrivateKeyAccess keyAccess, RSA? verificationKey = null)
    {
        _keyAccess = keyAccess;
        _verificationKey = verificationKey;
    }

    /// <summary>
    /// Validates input, loads the key, signs the exact payload bytes, and
    /// assembles the license.lic envelope. Returns success/failure; never throws
    /// for expected invalid input.
    /// </summary>
    public LicenseSignResult SignLicense(string schoolName, string hwid, string expires, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(schoolName))
        {
            return LicenseSignResult.Failure("Enter a school name.");
        }

        if (schoolName.Length > 150)
        {
            return LicenseSignResult.Failure("School name must be 150 characters or fewer.");
        }

        string normalized = MachineIdNormalizer.Normalize(hwid);
        if (normalized.Length == 0)
        {
            return LicenseSignResult.Failure("Enter a valid hardware ID.");
        }

        if (!ExpiryRules.TryParseDateOnly(expires, out var expiry)
            || expires != expiry.ToString("yyyy-MM-dd"))
        {
            return LicenseSignResult.Failure("Select a valid expiration date (YYYY-MM-DD).");
        }

        if (expires.CompareTo(today.ToString("yyyy-MM-dd")) < 0)
        {
            return LicenseSignResult.Failure("Expiration date must be today or later.");
        }

        var keyCheck = _keyAccess.Check();
        if (!keyCheck.Safe)
        {
            return LicenseSignResult.Failure(keyCheck.Message);
        }

        try
        {
            byte[] payloadBytes = LicensePayloadWriter.WritePayloadBytes(schoolName, normalized, expires);
            string payloadText = Encoding.UTF8.GetString(payloadBytes);

            using var privateKey = _keyAccess.Load();
            byte[] signature = privateKey.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            // Compatibility gate: if this key is not the one the Axumera consumer
            // will verify against, the generated license is useless. Cross-check
            // the signature against the production public key.
            bool ownsVerifier = _verificationKey is null;
            var verifier = ownsVerifier ? ProductionKeys.CreatePublicKey() : _verificationKey!;
            try
            {
                if (!verifier.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    return LicenseSignResult.Failure("The signing key does not match the Axumera production public key. Licenses signed with it would be rejected.");
                }
            }
            finally
            {
                if (ownsVerifier)
                {
                    verifier.Dispose();
                }
            }

            string payloadB64 = Convert.ToBase64String(payloadBytes);
            string signatureB64 = Convert.ToBase64String(signature);
            byte[] envelope = LicensePayloadWriter.WriteEnvelopeBytes(payloadB64, signatureB64);
            return LicenseSignResult.Success(payloadText, payloadB64, signatureB64, Encoding.UTF8.GetString(envelope));
        }
        catch (UnauthorizedAccessException)
        {
            return LicenseSignResult.Failure("The private key is not readable by the current user.");
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or IOException)
        {
            return LicenseSignResult.Failure("Unable to sign the license: " + ex.Message);
        }
    }
}