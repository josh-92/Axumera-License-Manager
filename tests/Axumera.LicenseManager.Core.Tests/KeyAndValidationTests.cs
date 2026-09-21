using System.Security.AccessControl;
using System.Security.Principal;
using Axumera.LicenseManager.Core.Crypto;
using Axumera.LicenseManager.Core.Models;
using Axumera.LicenseManager.Core.Rules;
using Axumera.LicenseManager.Core.Security;
using Xunit;

namespace Axumera.LicenseManager.Core.Tests;

public class PrivateKeyAccessTests
{
    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "AxumeraLM-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteKeyFile(string dir, string pem)
    {
        string path = Path.Combine(dir, "private_key.pem");
        File.WriteAllText(path, pem);
        return path;
    }

    [Fact]
    public void Not_configured_blocks_signing()
    {
        var access = new PrivateKeyAccess(null);
        var check = access.Check();
        Assert.False(check.Safe);
        Assert.False(access.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => access.Load());
    }

    [Fact]
    public void Missing_file_is_reported()
    {
        var access = new PrivateKeyAccess(Path.Combine(NewTempDir(), "does-not-exist.pem"));
        Assert.False(access.Check().Safe);
    }

    [Fact]
    public void Restrictive_key_file_passes_check_and_loads()
    {
        string dir = NewTempDir();
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        string pem = rsa.ExportPkcs8PrivateKeyPem();
        string path = WriteKeyFile(dir, pem);
        AclGuard.RestrictFile(path);

        var access = new PrivateKeyAccess(path);
        var check = access.Check();
        Assert.True(check.Safe);
        using var loaded = access.Load();
        Assert.Contains("BEGIN PRIVATE KEY", loaded.ExportPkcs8PrivateKeyPem(), StringComparison.Ordinal);
    }

    [Fact]
    public void Everyone_read_access_is_a_failure()
    {
        var owner = WindowsIdentity.GetCurrent();
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        string dir = NewTempDir();
        string path = WriteKeyFile(dir, rsa.ExportPkcs8PrivateKeyPem());

        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(owner.User!);
        security.AddAccessRule(new FileSystemAccessRule(owner.User!, FileSystemRights.FullControl, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.ReadAndExecute, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);

        var access = new PrivateKeyAccess(path);
        var check = access.Check();
        Assert.False(check.Safe);
        Assert.Contains("Everyone", check.Message);
    }

    [Fact]
    public void Tests_using_ephemeral_key_round_trip_through_signer_and_verify()
    {
        using var keys = TestKeyPair.Generate();
        string dir = NewTempDir();
        var access = new PrivateKeyAccess(WriteKeyFile(dir, keys.PrivatePem));
        AclGuard.RestrictFile(access.ConfiguredPath!);

        var signer = new LicenseSigner(access, verificationKey: keys.Public);
        var result = signer.SignLicense("ACL Test School", "ac-12", "2027-01-15", new DateOnly(2026, 9, 21));
        Assert.True(result.Ok, result.Error);

        var verification = LicenseVerifier.VerifyText(result.LicenseText!, keys.Public, expectedMachineId: "ac-12");
        Assert.True(verification.Verified, verification.Error);
        Assert.Equal("AC12", verification.HardwareId);
    }
}

public class LicenseSignerValidationTests
{
    private static readonly DateOnly Today = new(2026, 9, 21);

    private static LicenseSigner Signer()
    {
        var dir = Path.Combine(Path.GetTempPath(), "AxumeraLM-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        string privatePem = rsa.ExportPkcs8PrivateKeyPem();
        string path = Path.Combine(dir, "private_key.pem");
        File.WriteAllText(path, privatePem);
        AclGuard.RestrictFile(path);
        // Deliberately NOT disposed here: the signer's compatibility gate needs a
        // live public key for the duration of each test.
        var publicKey = System.Security.Cryptography.RSA.Create();
        publicKey.ImportFromPem(rsa.ExportSubjectPublicKeyInfoPem());
        return new LicenseSigner(new PrivateKeyAccess(path), verificationKey: publicKey);
    }

    [Fact]
    public void Empty_or_whitespace_school_name_is_rejected()
    {
        var signer = Signer();
        Assert.False(signer.SignLicense("", "ABC", "2027-01-01", Today).Ok);
        Assert.False(signer.SignLicense("   ", "ABC", "2027-01-01", Today).Ok);
    }

    [Fact]
    public void Overlong_school_name_is_rejected()
    {
        var signer = Signer();
        string longName = new string('x', 151);
        var result = signer.SignLicense(longName, "ABC", "2027-01-01", Today);
        Assert.False(result.Ok);
        Assert.Contains("150", result.Error);
    }

    [Fact]
    public void Empty_hwid_after_normalization_is_rejected()
    {
        var signer = Signer();
        Assert.False(signer.SignLicense("School", "---", "2027-01-01", Today).Ok);
    }

    [Fact]
    public void Past_expiry_is_rejected_today_is_accepted()
    {
        var signer = Signer();
        Assert.False(signer.SignLicense("School", "ABC", "2026-09-20", Today).Ok);
        var ok = signer.SignLicense("School", "ABC", "2026-09-21", Today);
        Assert.True(ok.Ok, ok.Error);
    }

    [Fact]
    public void Not_configured_signer_fails_cleanly()
    {
        var signer = new LicenseSigner(new PrivateKeyAccess(null));
        var result = signer.SignLicense("School", "ABC", "2027-01-01", Today);
        Assert.False(result.Ok);
        Assert.Contains("Settings", result.Error);
    }
}

public class ProductionKeysTests
{
    [Fact]
    public void Embedded_public_key_is_2048_bit_rsa()
    {
        using var rsa = ProductionKeys.CreatePublicKey();
        Assert.Equal(2048, rsa.KeySize);
        Assert.Equal(System.Security.Cryptography.RSA.Create().KeySize, rsa.KeySize);
    }

    [Fact]
    public void License_signed_by_foreign_key_is_rejected_by_production_key()
    {
        // The embedded production public key must reject a signature made by any
        // non-production key. (The positive path needs the real private key and is
        // exercised only by the build cross-check script.)
        using var foreign = TestKeyPair.Generate();
        byte[] payload = System.Text.Encoding.UTF8.GetBytes("{\"school_name\":\"X\",\"hwid\":\"A\",\"expires\":\"2030-01-01\"}");
        byte[] signature = foreign.Private.SignData(payload, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        string envelope = "{\"payload\":\"" + Convert.ToBase64String(payload) + "\",\"signature\":\"" + Convert.ToBase64String(signature) + "\"}";

        var production = ProductionKeys.CreatePublicKey();
        try
        {
            var verification = LicenseVerifier.VerifyText(envelope, production, expectedMachineId: null);
            Assert.False(verification.Verified);
        }
        finally
        {
            production.Dispose();
        }
    }
}