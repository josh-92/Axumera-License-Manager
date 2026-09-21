using System.Text;

namespace Axumera.LicenseManager.Core.Crypto;

/// <summary>
/// Byte-exact builder for the signed license payload.
///
/// <para>
/// The payload must be EXACTLY the raw JSON string the PHP generator produces
/// with <c>json_encode([...], JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE)</c>:
/// </para>
/// <code>{"school_name":"…","hwid":"…","expires":"YYYY-MM-DD"}</code>
/// <para>
/// Key order is fixed (school_name, hwid, expires), there is no whitespace,
/// Unicode is emitted as raw UTF-8, and only the characters PHP escapes are
/// escaped. Every consumer verifies the signature over these exact bytes; any
/// reformatting silently invalidates the license.
/// </para>
/// </summary>
public static class LicensePayloadWriter
{
    /// <summary>Escaping PHP applies to a JSON string member value (with matching flags).</summary>
    public static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length + 16);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append("\\u00").Append(((int)ch).ToString("x2"));
                    }
                    else
                    {
                        sb.Append(ch);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Writes the exact payload JSON for the given (already normalized) values
    /// and returns its UTF-8 bytes — the bytes that are base64'd into
    /// <c>payload</c> and signed by RSA-SHA256.
    /// </summary>
    public static byte[] WritePayloadBytes(string schoolName, string normalizedHwid, string expires)
    {
        var text = WritePayloadText(schoolName, normalizedHwid, expires);
        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>Returns the payload as text (exact string form).</summary>
    public static string WritePayloadText(string schoolName, string normalizedHwid, string expires)
    {
        return "{\"school_name\":\"" + Escape(schoolName)
            + "\",\"hwid\":\"" + Escape(normalizedHwid)
            + "\",\"expires\":\"" + Escape(expires) + "\"}";
    }

    /// <summary>
    /// Writes the outer license file envelope <c>{"payload":"…","signature":"…"}</c>.
    /// Both members are always ASCII base64, mirroring the generator's
    /// <c>json_encode(..., JSON_UNESCAPED_SLASHES)</c>.
    /// </summary>
    public static byte[] WriteEnvelopeBytes(string payloadB64, string signatureB64)
    {
        var text = "{\"payload\":\"" + payloadB64 + "\",\"signature\":\"" + signatureB64 + "\"}";
        return Encoding.UTF8.GetBytes(text);
    }
}