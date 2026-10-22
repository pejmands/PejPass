namespace PejPass.Application.Security;

/// <summary>
/// Update-signing public keys the app trusts (Base64 SubjectPublicKeyInfo, ECDSA P-256).
/// A manifest is accepted when ANY of its signatures verifies with ANY key listed here.
/// This file is compiled into both the app and tools/PejPass.UpdateSigner.
/// Keep this list short (at most three): every trusted key is as powerful as the weakest one.
/// </summary>
public static class TrustedUpdateKeys
{
    public static IReadOnlyList<string> PublicKeysBase64 { get; } =
    [
        // K1 (current). Copy this value from the old TrustedPublicKeyBase64 constant; do not retype it.
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAExWfWThPa8RrjVMZbubArG4H7vwym/rXR3HcXdl2lajZ7lfT0HfnNYItF7JEQmdPB/3Frhx2t42H8VosD+QrvoA=="
    ];
}
