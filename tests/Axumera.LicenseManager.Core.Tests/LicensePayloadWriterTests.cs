using System.Text;
using Axumera.LicenseManager.Core.Crypto;
using Xunit;

namespace Axumera.LicenseManager.Core.Tests;

public class LicensePayloadWriterTests
{
    [Fact]
    public void Simple_payload_is_byte_exact()
    {
        const string expected = "{\"school_name\":\"Don Bosco Catholic School\",\"hwid\":\"ABC123\",\"expires\":\"2027-09-21\"}";
        var bytes = LicensePayloadWriter.WritePayloadBytes("Don Bosco Catholic School", "ABC123", "2027-09-21");
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes));
        Assert.Equal(expected, LicensePayloadWriter.WritePayloadText("Don Bosco Catholic School", "ABC123", "2027-09-21"));
    }

    [Fact]
    public void Base64_of_payload_roundtrips_to_the_exact_string()
    {
        var bytes = LicensePayloadWriter.WritePayloadBytes("Ecole Test", "H3X5F", "2030-01-01");
        string b64 = Convert.ToBase64String(bytes);
        Assert.Equal("{\"school_name\":\"Ecole Test\",\"hwid\":\"H3X5F\",\"expires\":\"2030-01-01\"}", Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
    }

    [Fact]
    public void Key_order_is_fixed_school_then_hwid_then_expires()
    {
        var text = LicensePayloadWriter.WritePayloadText("A", "B", "C");
        Assert.StartsWith("{\"school_name\":\"A\",\"hwid\":\"B\",\"expires\":\"C\"}", text);
        Assert.True(text.IndexOf("school_name") < text.IndexOf("hwid"));
        Assert.True(text.IndexOf("hwid") < text.IndexOf("expires"));
    }

    [Fact]
    public void Quotes_and_backslashes_are_escaped_like_php()
    {
        var text = LicensePayloadWriter.Escape("Say \"hi\" and use a \\ slash");
        Assert.Equal("Say \\\"hi\\\" and use a \\\\ slash", text);
    }

    [Fact]
    public void Slashes_and_plus_signs_are_not_escaped()
    {
        Assert.Equal("path/to//thing+plus", LicensePayloadWriter.Escape("path/to//thing+plus"));
        Assert.Equal("a=b&c<d>e'f", LicensePayloadWriter.Escape("a=b&c<d>e'f"));
    }

    [Fact]
    public void Unicode_is_emitted_as_raw_utf8_not_escaped()
    {
        var text = LicensePayloadWriter.Escape("École — አክሱም");
        var bytes = Encoding.UTF8.GetBytes(text);
        // No '\u' escape sequences anywhere.
        Assert.DoesNotContain("\\u", text);
        // É must appear as its raw UTF-8 bytes (C3 89).
        Assert.Contains(new byte[] { 0xC3, 0x89 }, bytes);
    }

    [Fact]
    public void Control_characters_use_php_short_and_unicode_escapes()
    {
        Assert.Equal("\\n", LicensePayloadWriter.Escape("\n"));
        Assert.Equal("\\t", LicensePayloadWriter.Escape("\t"));
        Assert.Equal("\\r", LicensePayloadWriter.Escape("\r"));
        Assert.Equal("\\b", LicensePayloadWriter.Escape("\b"));
        Assert.Equal("\\f", LicensePayloadWriter.Escape("\f"));
        Assert.Equal("\\u001b", LicensePayloadWriter.Escape("\u001b"));
        Assert.Equal("\\u0001", LicensePayloadWriter.Escape("\u0001"));
    }

    [Fact]
    public void Envelope_has_payload_then_signature_ascii_base64()
    {
        var bytes = LicensePayloadWriter.WriteEnvelopeBytes("cGF5bG9hZA==", "c2lnbmF0dXJl");
        Assert.Equal("{\"payload\":\"cGF5bG9hZA==\",\"signature\":\"c2lnbmF0dXJl\"}", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Lowercase_unicode_escapes_are_used_for_short_controls()
    {
        Assert.Equal("\\u001f", LicensePayloadWriter.Escape("\u001f"));
        Assert.Equal("\\u0000", LicensePayloadWriter.Escape("\u0000"));
    }
}