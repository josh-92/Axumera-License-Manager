using System.Security.AccessControl;
using System.Security.Principal;

namespace Axumera.LicenseManager.Core.Security;

/// <summary>
/// Windows ACL hardening helpers for the app-data store and the signing key.
///
/// <para>
/// Documented safety rule for the private key file (must be preserved): the key
/// is blocked from use unless (a) it exists and is readable by the current
/// user, (b) no ACE grants ANY access to <c>Everyone</c>, and (c) no ACE grants
/// write-capable rights (Write/Modify/FullControl/ChangePermissions/Delete/
/// GenericWrite/GenericAll) to any principal other than the current user,
/// Administrators, SYSTEM, TrustedInstaller, OWNER RIGHTS or Creator Owner.
/// Read-only access for broader principals is tolerated but surfaced as a
/// warning so the operator can harden it.
/// </para>
/// </summary>
public static class AclGuard
{
    // Well-known SIDs (constant across all Windows installs, always resolvable).
    private static readonly SecurityIdentifier SidEveryone = new("S-1-1-0");
    private static readonly SecurityIdentifier SidAuthenticatedUsers = new("S-1-5-11");
    private static readonly SecurityIdentifier SidUsers = new("S-1-5-32-545");
    private static readonly SecurityIdentifier SidAdministrators = new("S-1-5-32-544");
    private static readonly SecurityIdentifier SidSystem = new("S-1-5-18");
    private static readonly SecurityIdentifier SidOwnerRights = new("S-1-3-4");
    private static readonly SecurityIdentifier SidCreatorOwner = new("S-1-3-0");
    private static readonly SecurityIdentifier SidTrustedInstaller = new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    public static SecurityIdentifier CurrentUserSid
        => WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Cannot resolve the current user's security identifier.");

    private static readonly SecurityIdentifier[] TrustedWriteSids =
    {
        SidAdministrators,
        SidSystem,
        SidOwnerRights,
        SidCreatorOwner,
        SidTrustedInstaller,
    };

    private static readonly FileSystemRights WriteCapable =
        FileSystemRights.Write |
        FileSystemRights.WriteData |
        FileSystemRights.AppendData |
        FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership |
        FileSystemRights.Modify |
        FileSystemRights.FullControl;

    /// <summary>Classifies a file's DACL.</summary>
    public static AclCheckResult CheckFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return AclCheckResult.Failure("The key file was not found.");
            }

            var acl = info.GetAccessControl();
            var current = CurrentUserSid;
            foreach (var rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule is not FileSystemAccessRule fs || fs.AccessControlType != AccessControlType.Allow)
                {
                    continue;
                }

                var sid = (SecurityIdentifier)fs.IdentityReference;

                if (sid == SidEveryone)
                {
                    return AclCheckResult.Failure("The key file is accessible by 'Everyone'. Use Settings → Restrict Access to harden it.");
                }

                if ((fs.FileSystemRights & WriteCapable) != 0 &&
                    sid != current &&
                    !TrustedWriteSids.Contains(sid))
                {
                    return AclCheckResult.Failure($"The key file grants write access to an untrusted principal ({Friendly(sid)}). Restrict the file access first.");
                }
            }

            // Readable by broader principals only → warn, do not block.
            foreach (var rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule is not FileSystemAccessRule fs
                    || fs.AccessControlType != AccessControlType.Allow
                    || (fs.FileSystemRights & ReadCapable) == 0
                    || fs.IdentityReference == current)
                {
                    continue;
                }

                var sid = (SecurityIdentifier)fs.IdentityReference;
                if (sid == SidEveryone || sid == SidAuthenticatedUsers || sid == SidUsers
                    || (fs.FileSystemRights & WriteCapable) != 0 && !TrustedWriteSids.Contains(sid))
                {
                    return AclCheckResult.Warn("The key file is readable by accounts other than the current user. Consider restricting it via Settings → Restrict Access.");
                }
            }

            return AclCheckResult.Ok();
        }
        catch (UnauthorizedAccessException)
        {
            return AclCheckResult.Failure("The key file's access rules cannot be read.");
        }
        catch (IOException)
        {
            return AclCheckResult.Failure("The key file's access could not be verified.");
        }
        catch (IdentityNotMappedException)
        {
            // A SID that cannot be resolved to a name is treated as unrecognized;
            // fall through to the read-access warning scan below.
            return AclCheckResult.Warn("The key file contains access rules for an unrecognized account. Consider restricting access via Settings → Restrict Access.");
        }
    }

    private static readonly FileSystemRights ReadCapable =
        FileSystemRights.Read |
        FileSystemRights.ReadAndExecute |
        FileSystemRights.ExecuteFile |
        FileSystemRights.ReadData |
        FileSystemRights.ReadAttributes |
        FileSystemRights.ReadExtendedAttributes |
        FileSystemRights.ReadPermissions |
        FileSystemRights.FullControl;

    /// <summary>
    /// Applies a restrictive DACL: only the current user, Administrators and
    /// SYSTEM keep access. No inheritance (the parent's rules no longer apply).
    /// Caller must own the file or hold WRITE_DAC (typical for the operator).
    /// </summary>
    public static void RestrictFile(string path)
    {
        var fs = new FileSecurity();
        fs.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        fs.SetOwner(CurrentUserSid);
        fs.AddAccessRule(new FileSystemAccessRule(CurrentUserSid, FileSystemRights.FullControl, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        fs.AddAccessRule(new FileSystemAccessRule(SidAdministrators, FileSystemRights.FullControl, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        fs.AddAccessRule(new FileSystemAccessRule(SidSystem, FileSystemRights.FullControl, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(fs);
    }

    /// <summary>
    /// Applies the same restrictive DACL to a directory (used for the tool's
    /// app-data folder so the account hash and license ledger stay private).
    /// </summary>
    public static void RestrictDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var ds = new DirectorySecurity();
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        ds.SetOwner(CurrentUserSid);
        ds.AddAccessRule(new FileSystemAccessRule(CurrentUserSid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(SidAdministrators, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(SidSystem, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(ds);
    }

    private static string Friendly(SecurityIdentifier sid)
    {
        try
        {
            return sid.Translate(typeof(NTAccount)).Value ?? sid.Value;
        }
        catch (IdentityNotMappedException)
        {
            return sid.Value;
        }
    }
}

/// <summary>Result of an ACL classification.</summary>
public sealed class AclCheckResult
{
    public bool Safe { get; init; }

    /// <summary>Non-empty when the file can be used but should be hardened.</summary>
    public bool Warning { get; init; }

    public string Message { get; init; } = string.Empty;

    public static AclCheckResult Ok() => new() { Safe = true };

    public static AclCheckResult Warn(string message) => new() { Safe = true, Warning = true, Message = message };

    public static AclCheckResult Failure(string message) => new() { Safe = false, Message = message };
}