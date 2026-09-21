using System.Security.Cryptography;

namespace Axumera.LicenseManager.Core.Crypto;

/// <summary>RSA key material helpers (PEM import/export).</summary>
public static class Keys
{
    /// <summary>Imports an RSAPrivateKey / PKCS#8 private key PEM (BEGIN PRIVATE KEY or BEGIN RSA PRIVATE KEY).</summary>
    public static RSA ImportPrivateKey(string pem)
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw new ArgumentException("The supplied PEM is not a valid RSA private key.", nameof(pem));
        }
    }

    /// <summary>Imports a subject-public-key-info PEM (BEGIN PUBLIC KEY).</summary>
    public static RSA ImportPublicKey(string pem)
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw new ArgumentException("The supplied PEM is not a valid RSA public key.", nameof(pem));
        }
    }

    /// <summary>Exports the current SPKI PEM of a key (used to fingerprint/mirror a key).</summary>
    public static string ExportSubjectPublicKeyInfoPem(RSA rsa)
        => rsa.ExportSubjectPublicKeyInfoPem();

    /// <summary>Exports a PKCS#8 PEM of a private key (used only for disposable test keys).</summary>
    public static string ExportPrivateKeyPkcs8Pem(RSA rsa)
        => rsa.ExportPkcs8PrivateKeyPem();
}