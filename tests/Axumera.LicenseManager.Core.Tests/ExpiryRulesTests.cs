using Axumera.LicenseManager.Core.Models;
using Axumera.LicenseManager.Core.Rules;
using Xunit;

namespace Axumera.LicenseManager.Core.Tests;

public class ExpiryRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 21);

    [Fact]
    public void Remaining_is_calendar_day_difference()
    {
        Assert.Equal(365, ExpiryRules.RemainingDays("2027-09-21", Today));
        Assert.Equal(1, ExpiryRules.RemainingDays("2026-09-22", Today));
        Assert.Equal(0, ExpiryRules.RemainingDays("2026-09-21", Today));
        Assert.Equal(-3, ExpiryRules.RemainingDays("2026-09-18", Today));
    }

    [Fact]
    public void Status_boundaries_match_documented_rule()
    {
        Assert.Equal(LicenseRecordStatus.Active, ExpiryRules.Status(31));
        Assert.Equal(LicenseRecordStatus.ExpiringSoon, ExpiryRules.Status(30));
        Assert.Equal(LicenseRecordStatus.ExpiringSoon, ExpiryRules.Status(1));
        Assert.Equal(LicenseRecordStatus.Expired, ExpiryRules.Status(0));
        Assert.Equal(LicenseRecordStatus.Expired, ExpiryRules.Status(-5));
    }

    [Fact]
    public void Yesterday_is_expired_today_is_zero_days_expired()
    {
        Assert.Equal(LicenseRecordStatus.Expired, ExpiryRules.Status("2026-09-20", Today));
        Assert.Equal(LicenseRecordStatus.Expired, ExpiryRules.Status("2026-09-21", Today));
        Assert.Equal(0, ExpiryRules.RemainingDays("2026-09-21", Today));
    }

    [Fact]
    public void DaysLabel_is_plural_and_handles_edge_cases()
    {
        Assert.Equal("365 days", ExpiryRules.DaysLabel(365));
        Assert.Equal("1 day", ExpiryRules.DaysLabel(1));
        Assert.Equal("0 days", ExpiryRules.DaysLabel(0));
        Assert.Equal("expired", ExpiryRules.DaysLabel(-1));
    }

    [Theory]
    [InlineData("2022-02-29")]
    [InlineData("bad-date")]
    [InlineData("2026-13-01")]
    public void Parse_rejects_invalid_dates(string value)
    {
        Assert.False(ExpiryRules.TryParseDateOnly(value, out _));
    }

    [Fact]
    public void Parse_accepts_valid_dates_and_normalizes_canonically()
    {
        Assert.True(ExpiryRules.TryParseDateOnly("2027-09-21", out var d1));
        Assert.Equal(new DateOnly(2027, 9, 21), d1);
        Assert.False(ExpiryRules.TryParseDateOnly("2027-9-1", out _)); // zero-padding required
    }
}