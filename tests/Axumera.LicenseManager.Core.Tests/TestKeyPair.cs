using System.Security.Cryptography;
using Axumera.LicenseManager.Core.Crypto;
using Axumera.LicenseManager.Core.Security;

namespace Axumera.LicenseManager.Core.Tests;

/// <summary>Disposable ephemeral RSA keypair used only by tests — never the production key.</summary>
internal sealed class TestKeyPair : IDisposable
{
    public RSA Private { get; }
    public RSA Public { get; }

    private TestKeyPair(string privatePem, string publicPem)
    {
        PrivatePem = privatePem;
        PublicPem = publicPem;
        Private = Crypto.Keys.ImportPrivateKey(privatePem);
        Public = Crypto.Keys.ImportPublicKey(publicPem);
    }

    public string PrivatePem { get; }
    public string PublicPem { get; }

    public static TestKeyPair Generate(int bits = 2048)
    {
        using var rsa = RSA.Create(bits);
        return new TestKeyPair(rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }

    public void Dispose()
    {
        Private.Dispose();
        Public.Dispose();
    }

    /// <summary>Writes the private key to a temp file with a restrictive ACL and returns a PrivateKeyAccess.</summary>
    public PrivateKeyAccess WritePrivateKeyToTemp(string dir, out string path)
    {
        Directory.CreateDirectory(dir);
        path = Path.Combine(dir, "test-key.pem");
        File.WriteAllText(path, PrivatePem);
        AclGuard.RestrictFile(path);
        return new PrivateKeyAccess(path);
    }
}

internal static class TempDir
{
    public static string New()
    {
        string dir = Path.Combine(Path.GetTempPath(), "AxumeraLM-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}