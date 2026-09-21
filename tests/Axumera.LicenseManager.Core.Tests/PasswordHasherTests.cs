using Axumera.LicenseManager.Core.Accounts;
using Xunit;

namespace Axumera.LicenseManager.Core.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_produces_prefixed_format_and_verifies()
    {
        string hash = PasswordHasher.Hash("Correct Horse Battery Staple");
        Assert.StartsWith("pbkdf2-sha256$600000$", hash);
        Assert.Equal(4, hash.Split('$').Length);
        Assert.True(PasswordHasher.Verify("Correct Horse Battery Staple", hash));
    }

    [Fact]
    public void Distinct_salts_make_distinct_hashes()
    {
        string a = PasswordHasher.Hash("same-password");
        string b = PasswordHasher.Hash("same-password");
        Assert.NotEqual(a, b);
        Assert.True(PasswordHasher.Verify("same-password", a));
        Assert.True(PasswordHasher.Verify("same-password", b));
    }

    [Fact]
    public void Wrong_password_is_rejected()
    {
        string hash = PasswordHasher.Hash("right-password");
        Assert.False(PasswordHasher.Verify("wrong-password", hash));
    }

    [Fact]
    public void Verify_tolerates_malformed_stored_hashes()
    {
        Assert.False(PasswordHasher.Verify("x", string.Empty));
        Assert.False(PasswordHasher.Verify("x", "not-a-hash"));
        Assert.False(PasswordHasher.Verify("x", "pbkdf2-sha256$abc$zzz$zzz"));
        Assert.False(PasswordHasher.Verify("x", "sha1$10$c2FsdA==$b2g="));
        Assert.False(PasswordHasher.Verify("x", "pbkdf2-sha256$20000000$c2FsdA==$b2g="));
        Assert.False(PasswordHasher.Verify(null, "pbkdf2-sha256$1$c2FsdA==$b2g="));
    }

    [Fact]
    public void Empty_password_cannot_be_hashed()
    {
        Assert.Throws<ArgumentException>(() => PasswordHasher.Hash(""));
    }

    [Fact]
    public void Too_long_password_cannot_be_hashed()
    {
        Assert.Throws<ArgumentException>(() => PasswordHasher.Hash(new string('x', PasswordHasher.MaxLength + 1)));
    }
}