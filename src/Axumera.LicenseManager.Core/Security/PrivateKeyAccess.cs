using System.Security.Cryptography;
using System.Text;

namespace Axumera.LicenseManager.Core.Security;

/// <summary>
/// The ONLY gateway to the live signing key. Governs path validation, ACL
/// safety and the load-for-one-operation lifecycle. The key is never copied,
/// embedded, exported or persisted anywhere by this tool.
/// </summary>
public sealed class PrivateKeyAccess
{
    private readonly string? _path;

    public PrivateKeyAccess(string? path) => _path = string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    public bool IsConfigured => _path is not null;

    public string? ConfiguredPath => _path;

    /// <summary>
    /// Verifies existence, readability and ACL safety. Safe=false blocks signing
    /// (per the documented rule in <see cref="AclGuard"/>).
    /// </summary>
    public AclCheckResult Check()
    {
        if (_path is null)
        {
            return AclCheckResult.Failure("No signing key is configured. Open Settings and select private_key.pem.");
        }

        var acl = AclGuard.CheckFile(_path);
        if (!acl.Safe)
        {
            return acl;
        }

        try
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length == 0)
            {
                return AclCheckResult.Failure("The signing key file is empty.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AclCheckResult.Failure("The signing key file cannot be read.");
        }

        return acl;
    }

    /// <summary>Loads the private key into memory. Callers must dispose; the key exists only for the signing operation.</summary>
    public RSA Load()
    {
        if (_path is null)
        {
            throw new InvalidOperationException("No signing key is configured.");
        }

        var acl = Check();
        if (!acl.Safe)
        {
            throw new UnauthorizedAccessException(acl.Message);
        }

        string pem = File.ReadAllText(_path, Encoding.UTF8);
        return Crypto.Keys.ImportPrivateKey(pem);
    }
}