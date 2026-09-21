using System.Globalization;
using Axumera.LicenseManager.Core.Models;

namespace Axumera.LicenseManager.Core.Rules;

/// <summary>
/// Deterministic expiry/countdown rules for the records dashboard.
///
/// <para>Documented behavior (must be preserved):</para>
/// <list type="number">
/// <item><c>remaining = expires.DayNumber - today.DayNumber</c> where both are
/// date-only values in the machine's local time zone. No time-of-day component
/// is involved, so an expiry on the same calendar day is always "0 days".</item>
/// <item>Status: <c>remaining &gt; 30</c> → Active; <c>1..30</c> → ExpiringSoon;
/// <c>&lt;= 0</c> → Expired (expiration today counts as expired).</item>
/// <item>The remaining count is NEVER persisted — it is recomputed from the
/// stored expiry date every time records are loaded or refreshed.</item>
/// </list>
/// </summary>
public static class ExpiryRules
{
    /// <summary>Literal status labels used by the UI (professional, non-flashy).</summary>
    public static string Label(LicenseRecordStatus status) => status switch
    {
        LicenseRecordStatus.Active => "ACTIVE",
        LicenseRecordStatus.ExpiringSoon => "EXPIRING SOON",
        LicenseRecordStatus.Expired => "EXPIRED",
        LicenseRecordStatus.Archived => "ARCHIVED",
        _ => "UNKNOWN",
    };

    public static int RemainingDays(string expires, DateOnly today) =>
        RemainingDays(ParseDateOnly(expires), today);

    public static int RemainingDays(DateOnly expires, DateOnly today) =>
        expires.DayNumber - today.DayNumber;

    public static LicenseRecordStatus Status(string expires, DateOnly today)
        => Status(RemainingDays(expires, today));

    /// <summary>30-day boundary: ≤30 remaining = expiring soon; expiry reached (≤0) = expired.</summary>
    public static LicenseRecordStatus Status(int remainingDays) => remainingDays switch
    {
        > 30 => LicenseRecordStatus.Active,
        > 0 => LicenseRecordStatus.ExpiringSoon,
        _ => LicenseRecordStatus.Expired,
    };

    /// <summary>Human label like "365 days", "1 day", "0 days", "expired".</summary>
    public static string DaysLabel(int remainingDays) => remainingDays switch
    {
        < 0 => "expired",
        0 => "0 days",
        1 => "1 day",
        _ => $"{remainingDays} days",
    };

    /// <summary>Today-aware convenience for display code (UI). Deterministic overloads above stay in tests.</summary>
    public static int RemainingDays(string expires) => RemainingDays(expires, DateOnly.FromDateTime(DateTime.Now));

    public static string DaysLabel(string expires) => DaysLabel(RemainingDays(expires));

    public static DateOnly ParseDateOnly(string yyyyMmDd)
    {
        if (DateOnly.TryParseExact(yyyyMmDd, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            return value;
        }

        throw new FormatException($"'{yyyyMmDd}' is not a valid yyyy-MM-dd date.");
    }

    public static bool TryParseDateOnly(string yyyyMmDd, out DateOnly value)
        => DateOnly.TryParseExact(yyyyMmDd, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}