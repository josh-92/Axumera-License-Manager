using System.Security.Cryptography;
using System.Text.Json;
using Axumera.LicenseManager.Core.Models;
using Axumera.LicenseManager.Core.Rules;

namespace Axumera.LicenseManager.Core.Crypto;

/// <summary>
/// Verifies a license.lic envelope exactly the way the Axumera consumer does:
/// parse outer JSON → strict base64-decode both fields → RSA-SHA256 over the
/// EXACT decoded payload bytes → only then parse the payload and evaluate the
/// contained fields. Reports malformed/tampered/expired/wrong-key outcomes
/// without leaking secrets.
/// </summary>
public static class LicenseVerifier
{
    /// <summary>Verifies a license file using the production public key.</summary>
    public static LicenseVerificationResult VerifyFile(string path)
        => VerifyFile(path, ProductionKeys.CreatePublicKey(), expectedMachineId: null);

    public static LicenseVerificationResult VerifyFile(string path, RSA publicKey, string? expectedMachineId)
    {
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LicenseVerificationResult.Invalid("The license file could not be read.");
        }

        return VerifyText(content, publicKey, expectedMachineId);
    }

    public static LicenseVerificationResult VerifyText(string content, RSA publicKey, string? expectedMachineId)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return LicenseVerificationResult.Invalid("The license file is empty.");
        }

        JsonDocument envelope;
        try
        {
            envelope = JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            return LicenseVerificationResult.Invalid("The license file is not valid JSON.");
        }

        using (envelope)
        {
            if (envelope.RootElement.ValueKind != JsonValueKind.Object
                || !envelope.RootElement.TryGetProperty("payload", out var payloadEl) || payloadEl.ValueKind != JsonValueKind.String
                || !envelope.RootElement.TryGetProperty("signature", out var signatureEl) || signatureEl.ValueKind != JsonValueKind.String)
            {
                return LicenseVerificationResult.Invalid("The license file must contain 'payload' and 'signature' string fields.");
            }

            string payloadB64 = payloadEl.GetString()!;
            string signatureB64 = signatureEl.GetString()!;

            byte[] payload;
            byte[] signature;
            try
            {
                payload = Convert.FromBase64String(payloadB64);
                signature = Convert.FromBase64String(signatureB64);
            }
            catch (FormatException)
            {
                return LicenseVerificationResult.Invalid("The license file contains invalid base64 data.");
            }

            bool verified;
            try
            {
                verified = publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch (CryptographicException)
            {
                return LicenseVerificationResult.Invalid("The license signature could not be evaluated.");
            }

            if (!verified)
            {
                return LicenseVerificationResult.Invalid("The license signature is not valid. The file may be tampered with or was signed by a different key.");
            }

            // Signature is valid over the exact payload bytes — now parse the payload.
            return ParsePayload(payload, expectedMachineId);
        }
    }

    private static LicenseVerificationResult ParsePayload(byte[] payload, string? expectedMachineId)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return LicenseVerificationResult.Invalid("The signed payload is not valid JSON.");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("school_name", out var school) || school.ValueKind != JsonValueKind.String
                || !doc.RootElement.TryGetProperty("hwid", out var hwid) || hwid.ValueKind != JsonValueKind.String
                || !doc.RootElement.TryGetProperty("expires", out var expires) || expires.ValueKind != JsonValueKind.String)
            {
                return LicenseVerificationResult.Invalid("The signed payload is missing 'school_name', 'hwid' or 'expires'.");
            }

            string schoolName = school.GetString()!;
            string hwidValue = hwid.GetString()!;
            string expiresValue = expires.GetString()!;

            if (!ExpiryRules.TryParseDateOnly(expiresValue, out var expiry) || expiry.ToString("yyyy-MM-dd") != expiresValue)
            {
                return LicenseVerificationResult.Invalid("The signed payload has an invalid expiration date.");
            }

            if (!MachineIdNormalizer.IsValid(hwidValue))
            {
                return LicenseVerificationResult.Invalid("The signed payload has an empty hardware ID.");
            }

            var today = DateOnly.FromDateTime(DateTime.Now);
            bool expired = expiresValue.CompareTo(today.ToString("yyyy-MM-dd")) < 0;

            bool? machineMatches = null;
            if (!string.IsNullOrWhiteSpace(expectedMachineId))
            {
                machineMatches = MachineIdNormalizer.Normalize(expectedMachineId) == MachineIdNormalizer.Normalize(hwidValue);
            }

            return new LicenseVerificationResult
            {
                Verified = true,
                SchoolName = schoolName,
                HardwareId = hwidValue,
                Expires = expiresValue,
                Expired = expired,
                MachineMatches = machineMatches,
            };
        }
    }
}