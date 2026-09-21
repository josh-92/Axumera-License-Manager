using Axumera.LicenseManager.Core.Rules;
using Xunit;

namespace Axumera.LicenseManager.Core.Tests;

public class MachineIdNormalizerTests
{
    [Theory]
    [InlineData(" abc-123 ", "ABC123")]
    [InlineData("e4d5f6G7-8H9I-ABCD-EF01-23456789ABCD", "E4D5F6G78H9IABCDEF0123456789ABCD")]
    [InlineData("Lower-case", "LOWERCASE")]
    [InlineData("!!hello..world__99", "HELLOWORLD99")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("---", "")]
    public void Normalize_matches_php_generator(string input, string expected)
    {
        Assert.Equal(expected, MachineIdNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_strips_unicode_letters_too()
    {
        // PHP /[^A-Za-z0-9]/ keeps only ASCII alphanumeric — é and 中 must be stripped.
        Assert.Equal("ABC", MachineIdNormalizer.Normalize("AéB中C"));
    }

    [Fact]
    public void IsValid_requires_nonempty_result()
    {
        Assert.True(MachineIdNormalizer.IsValid("abc123"));
        Assert.False(MachineIdNormalizer.IsValid(null));
        Assert.False(MachineIdNormalizer.IsValid("---"));
    }
}