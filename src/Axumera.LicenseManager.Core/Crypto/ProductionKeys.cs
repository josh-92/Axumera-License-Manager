namespace Axumera.LicenseManager.Core.Crypto;

/// <summary>
/// The production Axumera license-key material this tool must be compatible with.
///
/// <para>
/// This is the PUBLIC half only — the SPKI of the live 2048-bit RSA key
/// (e=65537). It is byte-identical to the consumer's
/// <c>application/eaes_exam_system/app/Keys/public_key.pem</c> (which lives in
/// the separate Axumera Exam Suite repo). The private key is NEVER embedded in
/// this assembly; it is loaded on demand from an operator-configured path.
/// </para>
/// </summary>
public static class ProductionKeys
{
    public const string PublicKeyPem =
        """
        -----BEGIN PUBLIC KEY-----
        MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA3C3ajEwzUlw3KcLmlRq4
        RuumkYdqMy0xT6u/v/llavmfT4ZGLNIT1vKqsgIkT4a0Lwb5T3xn74ih2xlkPwh8
        bPsuOUOYoevbfIU4DdE/7c8A2vwsqRB5BT5MbuJxpLu+oreYexLmok0E2S8j6f2Y
        h7IpvGqIXnfEmMLMhIKFKMUpoWOuVmAOAeRGCBbeuef604UhDxrpGUkl9AiYG4WP
        u7ILkDhFFw1Ac6ytrz71ot5BtgsmGXfaPo6JShkkV6uRyeR/PcE/j6RSOoLod5h4
        nBF34oc+Qa+0+HE1NMByt9VsZYYVDMxQaZmAzKlwKlgtaUAHPv9x0pZuIRINELt/
        XQIDAQAB
        -----END PUBLIC KEY-----
        """;

    /// <summary>Loads the production public key for signature verification.</summary>
    public static System.Security.Cryptography.RSA CreatePublicKey()
        => Crypto.Keys.ImportPublicKey(PublicKeyPem);
}