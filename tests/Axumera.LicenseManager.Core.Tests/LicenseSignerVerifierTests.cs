using System.Security.Cryptography;
using Axumera.LicenseManager.Core.Crypto;
using Axumera.LicenseManager.Core.Models;
using Axumera.LicenseManager.Core.Rules;
using Axumera.LicenseManager.Core.Security;
using Xunit;

namespace Axumera.LicenseManager.Core.Tests;

public class LicenseSignerVerifierTests
{
    private static readonly DateOnly Today = new(2026, 9, 21);

    private static LicenseSigner BuildSigner(TestKeyPair keys, string dir)
    {
        keys.WritePrivateKeyToTemp(dir, out _);
        var access = new PrivateKeyAccess(Path.Combine(dir, "test-key.pem"));
        return new LicenseSigner(access, verificationKey: keys.Public);
    }

    [Fact]
    public void Sign_then_verify_roundtrip()
    {
        using var keys = TestKeyPair.Generate();
        var signer = new LicenseSigner(CreateAccess(keys), keys.Public);

        var result = signer.SignLicense("Don Bosco Catholic School", "e4d5f6g7-8h9i", "2027-09-21", Today);
        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.LicenseText);

        var verification = LicenseVerifier.VerifyText(result.LicenseText!, keys.Public, expectedMachineId: "E4D5F6G7-8H9I".Replace("-", ""));
        Assert.True(verification.Verified, verification.Error);
        Assert.Equal("Don Bosco Catholic School", verification.SchoolName);
        Assert.Equal("E4D5F6G78H9I", verification.HardwareId);
        Assert.Equal("2027-09-21", verification.Expires);
        Assert.False(verification.Expired);
        Assert.True(verification.MachineMatches);
    }

    [Fact]
    public void Payload_base64_is_the_exact_json_string()
    {
        using var keys = TestKeyPair.Generate();
        var signer = new LicenseSigner(CreateAccess(keys), keys.Public);
        var result = signer.SignLicense("School Example", "ABC123", "2030-01-01", Today);
        Assert.True(result.Ok, result.Error);

        Assert.Equal("{\"school_name\":\"School Example\",\"hwid\":\"ABC123\",\"expires\":\"2030-01-01\"}",
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(result.PayloadBase64!)));
    }

    [Fact]
    public void Tampered_payload_is_rejected()
    {
        using var keys = TestKeyPair.Generate();
        var signer = new LicenseSigner(CreateAccess(keys), keys.Public);
        var result = signer.SignLicense("School One", "ABC123", "2027-06-01", Today);
        Assert.True(result.Ok, result.Error);

        // Flip one base64 char inside the payload field (uncorrupting padding avoids FormatException).
        string tampered = TamperBase64Member(result.LicenseText!, "payload");
        var verification = LicenseVerifier.VerifyText(tampered, keys.Public, expectedMachineId: null);
        Assert.False(verification.Verified);
    }

    [Fact]
    public void Tampered_signature_is_rejected()
    {
        using var keys = TestKeyPair.Generate();
        var signer = new LicenseSigner(CreateAccess(keys), keys.Public);
        var result = signer.SignLicense("School Two", "ABC123", "2027-06-01", Today);
        Assert.True(result.Ok, result.Error);

        string tampered = TamperBase64Member(result.LicenseText!, "signature");
        var verification = LicenseVerifier.VerifyText(tampered, keys.Public, expectedMachineId: null);
        Assert.False(verification.Verified);
        Assert.Null(verification.SchoolName);
    }

    [Fact]
    public void Wrong_public_key_is_rejected()
    {
        using var keys = TestKeyPair.Generate();
        using var other = TestKeyPair.Generate();
        var signer = new LicenseSigner(CreateAccess(keys), keys.Public);
        var result = signer.SignLicense("School Three", "ABC123", "2027-06-01", Today);
        Assert.True(result.Ok, result.Error);

        var verification = LicenseVerifier.VerifyText(result.LicenseText!, other.Public, expectedMachineId: null);
        Assert.False(verification.Verified);
    }

    [Fact]
    public void Expired_license_still_verifies_signature_but_reports_expired()
    {
        using var keys = TestKeyPair.Generate();
        // The signer refuses to generate past-dated licenses by design, so a
        // legacy/expired license is assembled directly (as the old PHP generator
        // would have produced it) and must still verify — but report Expired.
        byte[] payload = LicensePayloadWriter.WritePayloadBytes("Old School", "XYZ789", "2020-01-01");
        byte[] signature = keys.Private.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        string envelope = "{\"payload\":\"" + Convert.ToBase64String(payload) + "\",\"signature\":\"" + Convert.ToBase64String(signature) + "\"}";

        var verification = LicenseVerifier.VerifyText(envelope, keys.Public, expectedMachineId: null);
        Assert.True(verification.Verified, verification.Error);
        Assert.True(verification.Expired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"hello\": 1}")]
    [InlineData("{\"payload\": 1, \"signature\": \"x\"}")]
    public void Malformed_envelope_is_rejected(string content)
    {
        using var keys = TestKeyPair.Generate();
        var verification = LicenseVerifier.VerifyText(content, keys.Public, expectedMachineId: null);
        Assert.False(verification.Verified);
        Assert.NotNull(verification.Error);
    }

    [Fact]
    public void Invalid_base64_in_envelope_is_rejected()
    {
        using var keys = TestKeyPair.Generate();
        string bad = "{\"payload\":\"!!!\",\"signature\":\"!!!\"}";
        var verification = LicenseVerifier.VerifyText(bad, keys.Public, expectedMachineId: null);
        Assert.False(verification.Verified);
        Assert.Contains("base64", verification.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Signed_payload_missing_fields_is_rejected()
    {
        using var keys = TestKeyPair.Generate();
        byte[] tamperedPayload = System.Text.Encoding.UTF8.GetBytes("{\"school_name\":\"X\",\"hwid\":\"A\"}");
        byte[] signature = keys.Private.SignData(tamperedPayload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        string envelope = "{\"payload\":\"" + Convert.ToBase64String(tamperedPayload) + "\",\"signature\":\"" + Convert.ToBase64String(signature) + "\"}";

        var verification = LicenseVerifier.VerifyText(envelope, keys.Public, expectedMachineId: null);
        Assert.False(verification.Verified);
        Assert.Contains("missing", verification.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Machine_mismatch_is_reported_when_expected_machine_given()
    {
        using var keys = TestKeyPair.Generate();
        var signer = new LicenseSigner(CreateAccess(keys), keys.Public);
        var result = signer.SignLicense("School Four", "ABC-123", "2027-06-01", Today);
        Assert.True(result.Ok, result.Error);

        var match = LicenseVerifier.VerifyText(result.LicenseText!, keys.Public, expectedMachineId: "abc123");
        var mismatch = LicenseVerifier.VerifyText(result.LicenseText!, keys.Public, expectedMachineId: "ffffffff");
        Assert.True(match.MachineMatches);
        Assert.False(mismatch.MachineMatches);
    }

    private static PrivateKeyAccess CreateAccess(TestKeyPair keys)
    {
        string dir = Path.Combine(Path.GetTempPath(), "AxumeraLM-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "test-key.pem");
        File.WriteAllText(path, keys.PrivatePem);
        AclGuard.RestrictFile(path);
        return new PrivateKeyAccess(path);
    }

    private static string TamperBase64Member(string envelope, string member)
    {
        var doc = System.Text.Json.JsonDocument.Parse(envelope);
        string original;
        using (doc)
        {
            original = doc.RootElement.GetProperty(member).GetString()!;
        }

        char[] chars = original.ToCharArray();
        chars[^2] = chars[^2] == 'A' ? 'B' : 'A'; // flip one char, never a '=' padding position
        string patched = new(chars);
        var payload = System.Text.Json.JsonDocument.Parse(envelope).RootElement.GetProperty("payload").GetString()!;
        if (member == "payload")
        {
            return "{\"payload\":\"" + patched + "\",\"signature\":\"" + System.Text.Json.JsonDocument.Parse(envelope).RootElement.GetProperty("signature").GetString() + "\"}";
        }

        return "{\"payload\":\"" + payload + "\",\"signature\":\"" + patched + "\"}";
    }
}