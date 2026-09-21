using Axumera.LicenseManager.Core.Accounts;
using Xunit;

namespace Axumera.LicenseManager.Core.Tests;

public class AccountServiceTests
{
    private static AccountService NewService(out string file)
    {
        string dir = Path.Combine(Path.GetTempPath(), "AxumeraLM-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        file = Path.Combine(dir, "accounts.json");
        return new AccountService(file);
    }

    [Fact]
    public void First_run_creates_account_and_enables_login()
    {
        var svc = NewService(out _);
        Assert.False(svc.IsConfigured);

        var created = svc.CreateInitialAccount("admin", "password123", "password123");
        Assert.True(created.Ok, created.Error);
        Assert.True(svc.IsConfigured);
        Assert.True(svc.VerifyLogin("admin", "password123"));
        Assert.True(svc.VerifyLogin("ADMIN", "password123"));
        Assert.False(svc.VerifyLogin("admin", "wrong"));
        Assert.False(svc.VerifyLogin("other", "password123"));
    }

    [Fact]
    public void Duplicate_initial_account_is_refused()
    {
        var svc = NewService(out _);
        svc.CreateInitialAccount("admin", "password123", "password123");
        var second = svc.CreateInitialAccount("admin2", "password456", "password456");
        Assert.False(second.Ok);
        Assert.Equal("An administrator account already exists.", second.Error);
    }

    [Fact]
    public void Login_returns_canonical_username_only_for_verified_credentials()
    {
        var svc = NewService(out _);
        Assert.True(svc.CreateInitialAccount("AdminUser", "correctHorse9", "correctHorse9").Ok);

        // Case-insensitive username, case-preserved canonical result.
        Assert.Equal("AdminUser", svc.Login("adminuser", "correctHorse9"));

        // Every failure mode returns null with no distinguishing signal.
        Assert.Null(svc.Login("adminuser", "wrongPassword9"));
        Assert.Null(svc.Login("someoneElse", "correctHorse9"));
        Assert.Null(svc.Login("   ", "correctHorse9"));
        Assert.Null(svc.Login("adminuser", ""));
        Assert.Null(svc.Login("adminuser", null!));

        Assert.False(svc.VerifyLogin("adminuser", "wrongPassword9"));
    }

    [Fact]
    public void Random_credentials_are_always_rejected()
    {
        var svc = NewService(out _);
        Assert.True(svc.CreateInitialAccount("admin", "password123", "password123").Ok);

        foreach (string user in new[] { "admin", "alice", "Admin", "ADMIN", "x", "random-user-123" })
        {
            foreach (string pass in new[] { "password321", "abc12345", "totallyMadeUp", " ", "" })
            {
                Assert.False(svc.VerifyLogin(user, pass), $"Login '{user}'/'{pass}' must fail.");
                Assert.Null(svc.Login(user, pass));
            }
        }
    }

    [Theory]
    [InlineData(" ", "password123", "password123")]
    [InlineData("admin", "short", "short")]
    [InlineData("admin", "password123", "different")]
    public void Invalid_first_run_input_is_refused(string user, string pass, string confirm)
    {
        var svc = NewService(out _);
        var result = svc.CreateInitialAccount(user, pass, confirm);
        Assert.False(result.Ok);
        Assert.False(svc.IsConfigured);
    }

    [Fact]
    public void Password_change_requires_old_password()
    {
        var svc = NewService(out _);
        svc.CreateInitialAccount("admin", "oldPassword1", "oldPassword1");

        var wrong = svc.ChangePassword("admin", "notTheOldOne", "newPassword1", "newPassword1");
        Assert.False(wrong.Ok);
        Assert.False(svc.VerifyLogin("admin", "newPassword1"));
        Assert.True(svc.VerifyLogin("admin", "oldPassword1"));

        var ok = svc.ChangePassword("admin", "oldPassword1", "newPassword1", "newPassword1");
        Assert.True(ok.Ok, ok.Error);
        Assert.True(svc.VerifyLogin("admin", "newPassword1"));
        Assert.False(svc.VerifyLogin("admin", "oldPassword1"));
    }

    [Fact]
    public void Password_change_validates_new_password()
    {
        var svc = NewService(out _);
        svc.CreateInitialAccount("admin", "oldPassword1", "oldPassword1");

        Assert.False(svc.ChangePassword("admin", "oldPassword1", "short", "short").Ok);
        Assert.False(svc.ChangePassword("admin", "oldPassword1", "newPassword1", "different").Ok);

        // Mismatch must not clobber the stored record.
        Assert.True(svc.VerifyLogin("admin", "oldPassword1"));
    }

    [Fact]
    public void Accounts_file_is_never_plaintext_and_affected_arguments_are_safe()
    {
        var svc = NewService(out string file);
        svc.CreateInitialAccount("admin", "superSecret99", "superSecret99");

        string json = File.ReadAllText(file);
        Assert.DoesNotContain("superSecret99", json);
        Assert.Contains("passwordHash", json);

        // Hash file exists and is legal JSON.
        Assert.False(svc.VerifyLogin("admin; DROP TABLE users--", "superSecret99"));
    }

    [Fact]
    public void Store_state_classifies_first_run_vs_ready_vs_corrupt()
    {
        var svc = NewService(out string file);
        Assert.Equal(AccountStoreState.NotPresent, svc.State);
        Assert.False(svc.IsConfigured);

        var created = svc.CreateInitialAccount("admin", "password123", "password123");
        Assert.True(created.Ok, created.Error);
        Assert.Equal(AccountStoreState.Ready, svc.State);
        Assert.True(svc.IsConfigured);

        // Simulate a torn/corrupt file: state flips to Corrupt.
        File.WriteAllText(file, "{ not valid json [");
        Assert.Equal(AccountStoreState.Corrupt, svc.State);
        Assert.True(svc.IsConfigured); // a corrupt file is still "present"
    }

    [Fact]
    public void Corrupt_account_file_is_not_silently_replaced_and_blocks_login_and_setup()
    {
        var svc = NewService(out string file);
        svc.CreateInitialAccount("admin", "password123", "password123");
        string original = File.ReadAllText(file);

        File.WriteAllText(file, "garbage{");

        // Login must fail (no credential check can be trusted).
        Assert.False(svc.VerifyLogin("admin", "password123"));

        // First-run creation must NOT overwrite the corrupt file.
        var attempt = svc.CreateInitialAccount("admin2", "password456", "password456");
        Assert.False(attempt.Ok);

        // And the corrupt file must be left untouched.
        Assert.Equal("garbage{", File.ReadAllText(file));
        Assert.Equal(AccountStoreState.Corrupt, svc.State);
        _ = original;
    }

    [Fact]
    public void Restart_simulation_persists_hash_and_supports_login_and_password_change()
    {
        var svc = NewService(out string file);
        Assert.True(svc.CreateInitialAccount("admin", "oldPassword1", "oldPassword1").Ok);

        // New service instance over the same file == application restart.
        var fresh = new AccountService(file);
        Assert.True(fresh.IsConfigured);
        Assert.Equal(AccountStoreState.Ready, fresh.State);
        Assert.True(fresh.VerifyLogin("admin", "oldPassword1"));
        Assert.False(fresh.VerifyLogin("admin", "wrongPassword"));

        Assert.True(fresh.ChangePassword("admin", "oldPassword1", "newPassword1", "newPassword1").Ok);

        // Restart again: only the NEW password works.
        var afterChange = new AccountService(file);
        Assert.True(afterChange.VerifyLogin("admin", "newPassword1"));
        Assert.False(afterChange.VerifyLogin("admin", "oldPassword1"));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void Username_length_boundary_is_enforced(int length, bool expectedOk)
    {
        var svc = NewService(out _);
        var result = svc.CreateInitialAccount(new string('a', length), "password123", "password123");
        Assert.Equal(expectedOk, result.Ok);
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    [InlineData(7, false)]
    public void Password_length_boundary_is_enforced(int length, bool expectedOk)
    {
        var svc = NewService(out _);
        string password = new string('x', length);
        var result = svc.CreateInitialAccount("admin", password, password);
        Assert.Equal(expectedOk, result.Ok);
        if (expectedOk)
        {
            Assert.True(svc.VerifyLogin("admin", password));
        }
    }
}