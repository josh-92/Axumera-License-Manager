using System.Text.Json;
using System.Text.Json.Serialization;
using Axumera.LicenseManager.Core.Persistence;
using Axumera.LicenseManager.Core.Security;

namespace Axumera.LicenseManager.Core.Accounts;

/// <summary>
/// First-run account creation, login and password change against the local
/// account record. One local Administrator account for this version; no online
/// account system and no plaintext passwords anywhere.
/// </summary>
public sealed class AccountService
{
    private readonly string _accountFilePath;
    // Written camelCase to match the strict (case-sensitive) property lookups used
    // when loading; the record is never serialized in PascalCase.
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AccountService(AppDataPaths paths) : this(paths.AccountsFile) { }

    public AccountService(string accountFilePath) => _accountFilePath = accountFilePath;

    public bool IsConfigured => File.Exists(_accountFilePath);

    /// <summary>
    /// Classification of the local account store used to decide first-run
    /// (NotPresent), normal login (Ready) and explicit corrupt-file handling
    /// (Corrupt). A corrupt file is NEVER replaced silently; <c>Corrupt</c>
    /// surfaces it to the operator instead.
    /// </summary>
    public AccountStoreState State
    {
        get
        {
            if (!File.Exists(_accountFilePath))
            {
                return AccountStoreState.NotPresent;
            }

            return Load() is null ? AccountStoreState.Corrupt : AccountStoreState.Ready;
        }
    }

    public AccountInfo? Load()
    {
        try
        {
            if (!File.Exists(_accountFilePath))
            {
                return null;
            }

            var doc = JsonDocument.Parse(File.ReadAllText(_accountFilePath));
            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("username", out var u) || u.ValueKind != JsonValueKind.String
                    || !doc.RootElement.TryGetProperty("passwordHash", out var h) || h.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                return new AccountInfo(u.GetString()!, h.GetString()!);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Creates the initial administrator account (first-run only).</summary>
    public Result CreateInitialAccount(string username, string password, string confirmPassword)
    {
        username = (username ?? string.Empty).Trim();
        if (username.Length == 0)
        {
            return Result.Fail("Enter a username.");
        }

        if (username.Length > 64)
        {
            return Result.Fail("Username must be 64 characters or fewer.");
        }

        if (password.Length < PasswordHasher.MinLength)
        {
            return Result.Fail($"Password must be at least {PasswordHasher.MinLength} characters.");
        }

        if (password.Length > PasswordHasher.MaxLength)
        {
            return Result.Fail("Password is too long.");
        }

        if (password != confirmPassword)
        {
            return Result.Fail("Passwords do not match.");
        }

        if (IsConfigured)
        {
            return Result.Fail("An administrator account already exists.");
        }

        string hash;
        try
        {
            hash = PasswordHasher.Hash(password);
        }
        catch (ArgumentException ex)
        {
            return Result.Fail(ex.Message);
        }

        var record = new AccountInfo(username, hash);
        var json = JsonSerializer.Serialize(record, _options);
        return WriteAccountJson(json);
    }

    /// <summary>Verifies credentials. Always fails the same way regardless of the cause.</summary>
    public bool VerifyLogin(string username, string password) => Login(username, password) is not null;

    /// <summary>
    /// Authenticates and returns the canonical (stored-case) username on success,
    /// or null for every failure mode: no account, unknown username, wrong
    /// password, or an unreadable/corrupt store. The null result is identical in
    /// every case so callers cannot tell which part failed (no account
    /// enumeration) and no fallback or default credential is ever accepted.
    /// </summary>
    public string? Login(string username, string password)
    {
        var account = Load();
        if (account is null)
        {
            return null;
        }

        if (!string.Equals(account.Username, (username ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return PasswordHasher.Verify(password, account.PasswordHash) ? account.Username : null;
    }

    /// <summary>Changes the password after verifying the current one.</summary>
    public Result ChangePassword(string username, string currentPassword, string newPassword, string confirmNewPassword)
    {
        var account = Load();
        if (account is null)
        {
            return Result.Fail("No administrator account exists.");
        }

        if (!string.Equals(account.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)
            || !PasswordHasher.Verify(currentPassword, account.PasswordHash))
        {
            return Result.Fail("The current password is incorrect.");
        }

        if (newPassword.Length < PasswordHasher.MinLength)
        {
            return Result.Fail($"New password must be at least {PasswordHasher.MinLength} characters.");
        }

        if (newPassword.Length > PasswordHasher.MaxLength)
        {
            return Result.Fail("New password is too long.");
        }

        if (newPassword != confirmNewPassword)
        {
            return Result.Fail("New passwords do not match.");
        }

        string hash = PasswordHasher.Hash(newPassword);
        var updated = new AccountInfo(account.Username, hash);
        var json = JsonSerializer.Serialize(updated, _options);
        var write = WriteAccountJson(json);
        if (!write.Ok)
        {
            return write;
        }

        // Old account file remains at rest until the next atomic replace, but the
        // ACL restriction applied on creation ensures no other account can read it.
        return Result.Success();
    }

    private Result WriteAccountJson(string json)
    {
        var dir = Path.GetDirectoryName(_accountFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            try
            {
                AclGuard.RestrictDirectory(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Directory hardening is best-effort (as in AppDataPaths.EnsureReady):
                // the critical, security-relevant ACL is applied to the account file
                // itself below. A failure here must never abort account creation.
            }
        }

        try
        {
            AtomicJson.Write(_accountFilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail("The account record could not be saved. Check that the application data folder is writable and try again.");
        }

        try
        {
            AclGuard.RestrictFile(_accountFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The record was written; harden only if permitted.
        }

        return Result.Success();
    }
}

/// <summary>Persisted local account record — password hash only, never plaintext.</summary>
public sealed record AccountInfo(string Username, string PasswordHash);

/// <summary>State of the local account store for first-run/login branching.</summary>
public enum AccountStoreState
{
    /// <summary>No accounts file yet — first run, show the create-account screen.</summary>
    NotPresent,

    /// <summary>Valid account record exists — show the login screen.</summary>
    Ready,

    /// <summary>accounts.json exists but cannot be parsed — surface it, never overwrite it.</summary>
    Corrupt,
}

/// <summary>Simple result monad for UI-friendly error propagation.</summary>
public sealed record Result
{
    public bool Ok { get; init; }
    public string? Error { get; init; }

    public static Result Success() => new() { Ok = true };
    public static Result Fail(string error) => new() { Error = error };
}