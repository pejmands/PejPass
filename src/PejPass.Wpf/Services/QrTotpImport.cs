using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace PejPass.Wpf.Services;

/// <summary>
/// Decode a QR code from an image and extract a TOTP Base32 secret
/// (otpauth:// URI or raw secret text). Pattern aligned with Authenticator-style import.
/// </summary>
public static class QrTotpImport
{
    public sealed record Result(string Secret, string? Issuer, string? Account, string? RawText);

    public static bool TryDecode(BitmapSource image, out Result? result, out string? error)
    {
        result = null;
        error = null;

        try
        {
            var text = DecodeQrText(image);
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "No QR code found in the image.";
                return false;
            }

            if (!TryParseTotpPayload(text.Trim(), out result, out error))
                return false;

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryParseTotpPayload(string text, out Result? result, out string? error)
    {
        result = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Empty QR content.";
            return false;
        }

        text = text.Trim();

        // otpauth://totp/...
        if (text.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                error = "Invalid otpauth URI.";
                return false;
            }

            if (!string.Equals(uri.Scheme, "otpauth", StringComparison.OrdinalIgnoreCase))
            {
                error = "QR is not an otpauth URI.";
                return false;
            }

            // Host is typically "totp"; reject hotp for now
            var kind = uri.Host;
            if (!kind.Equals("totp", StringComparison.OrdinalIgnoreCase))
            {
                error = "Only TOTP (otpauth://totp/…) is supported.";
                return false;
            }

            var query = ParseQuery(uri.Query);
            if (!query.TryGetValue("secret", out var secret) || string.IsNullOrWhiteSpace(secret))
            {
                error = "otpauth URI has no secret parameter.";
                return false;
            }

            secret = secret.Replace(" ", "", StringComparison.Ordinal)
                          .Replace("-", "", StringComparison.Ordinal)
                          .Trim();

            string? issuer = query.TryGetValue("issuer", out var iss) ? Uri.UnescapeDataString(iss) : null;
            string? account = null;

            // Path: /Issuer:account or /account
            var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
            if (!string.IsNullOrEmpty(path))
            {
                var colon = path.IndexOf(':');
                if (colon > 0)
                {
                    var pathIssuer = path[..colon].Trim();
                    account = path[(colon + 1)..].Trim();
                    issuer ??= pathIssuer;
                }
                else
                {
                    account = path.Trim();
                }
            }

            result = new Result(secret, issuer, account, text);
            return true;
        }

        // Raw Base32 secret (some exports put only the key in the QR)
        var cleaned = text.Replace(" ", "", StringComparison.Ordinal)
                          .Replace("-", "", StringComparison.Ordinal)
                          .Trim();
        if (LooksLikeBase32(cleaned))
        {
            result = new Result(cleaned, null, null, text);
            return true;
        }

        error = "QR content is not a TOTP secret or otpauth:// URI.";
        return false;
    }

    private static string? DecodeQrText(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();

        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        if (width < 8 || height < 8)
            return null;

        var stride = width * 4;
        var bgra = new byte[height * stride];
        converted.CopyPixels(bgra, stride, 0);

        var rgb = new byte[width * height * 3];
        for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
        {
            rgb[j] = bgra[i + 2];     // R
            rgb[j + 1] = bgra[i + 1]; // G
            rgb[j + 2] = bgra[i];     // B
        }

        var luminance = new RGBLuminanceSource(rgb, width, height, RGBLuminanceSource.BitmapFormat.RGB24);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = [BarcodeFormat.QR_CODE]
            }
        };

        var decoded = reader.Decode(luminance);
        return decoded?.Text;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
            return map;

        var q = query.TrimStart('?');
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = Uri.UnescapeDataString(part[..eq]);
            var val = Uri.UnescapeDataString(part[(eq + 1)..]);
            map[key] = val;
        }

        return map;
    }

    private static bool LooksLikeBase32(string s)
    {
        if (s.Length < 8)
            return false;

        foreach (var c in s)
        {
            var u = char.ToUpperInvariant(c);
            if ((u < 'A' || u > 'Z') && (u < '2' || u > '7'))
                return false;
        }

        return true;
    }
}
