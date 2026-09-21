using System.Text;

namespace Axumera.LicenseManager.Core.Rules;

/// <summary>
/// Hardware-ID normalization shared by generator and consumer.
///
/// <para>
/// Contract (byte-compatible with the PHP keygen and the exam-suite consumer):
/// uppercase, and remove every character that is not ASCII alphanumeric:
/// <c>strtoupper(preg_replace('/[^A-Za-z0-9]/', '', $value))</c>. The consumer
/// must apply the IDENTICAL normalization to the machine's fingerprint or valid
/// licenses are rejected.
/// </para>
/// </summary>
public static class MachineIdNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                sb.Append(char.IsAsciiLetterLower(ch) ? char.ToUpperInvariant(ch) : ch);
            }
        }

        return sb.ToString();
    }

    public static bool IsValid(string? normalized) => Normalize(normalized).Length > 0;
}