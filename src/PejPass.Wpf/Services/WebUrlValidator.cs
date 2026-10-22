
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace PejPass.Wpf.Services;

public static class WebUrlValidator
{
    public static bool TryNormalize(
        string? url,
        out string normalizedUrl)
    {
        normalizedUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
            return false;

        url = url.Trim();

        if (url.Any(IsForbiddenUrlCharacter) ||
            !HasValidPercentEncoding(url))
        {
            return false;
        }

        var hasHttpScheme =
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

        var hasHttpsScheme =
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        string candidate;
        string authoritySource;

        if (hasHttpScheme || hasHttpsScheme)
        {
            var separatorIndex = url.IndexOf("://", StringComparison.Ordinal);

            if (separatorIndex < 0)
                return false;

            authoritySource = url[(separatorIndex + 3)..];
            candidate = url;
        }
        else
        {
            if (url.StartsWith("//", StringComparison.Ordinal) ||
                url.Contains("://", StringComparison.Ordinal))
            {
                return false;
            }

            authoritySource = url;
            candidate = "https://" + url;
        }

        var authority = ExtractAuthority(authoritySource);

        if (!IsValidAuthority(authority))
            return false;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        normalizedUrl = uri.AbsoluteUri;
        return true;
    }

    private static bool IsForbiddenUrlCharacter(char c)
    {
        return char.IsControl(c) ||
               char.IsWhiteSpace(c) ||
               c is '\\' or '<' or '>' or '"' or
                   '{' or '}' or '|' or '^' or '`';
    }

    private static bool HasValidPercentEncoding(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '%')
                continue;

            if (i + 2 >= value.Length ||
                !Uri.IsHexDigit(value[i + 1]) ||
                !Uri.IsHexDigit(value[i + 2]))
            {
                return false;
            }

            i += 2;
        }

        return true;
    }

    private static string ExtractAuthority(string value)
    {
        var endIndex = value.IndexOfAny(['/', '?', '#']);

        return endIndex < 0
            ? value
            : value[..endIndex];
    }

    private static bool IsValidAuthority(string authority)
    {
        if (string.IsNullOrEmpty(authority) ||
            authority.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) ||
            authority.Contains('@') ||
            authority.Contains('\\') ||
            authority.Contains('%'))
        {
            return false;
        }

        if (authority[0] == '[')
        {
            var closingBracket = authority.IndexOf(']');

            if (closingBracket <= 1 ||
                authority.IndexOf('[', 1) >= 0 ||
                authority.IndexOf(']', closingBracket + 1) >= 0)
            {
                return false;
            }

            var addressText = authority[1..closingBracket];

            if (!IPAddress.TryParse(addressText, out var address) ||
                address.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return false;
            }

            var suffix = authority[(closingBracket + 1)..];

            if (suffix.Length == 0)
                return true;

            return suffix[0] == ':' &&
                   IsValidPort(suffix[1..]);
        }

        if (authority.Contains('[') || authority.Contains(']'))
            return false;

        var colonIndex = authority.IndexOf(':');
        var host = authority;

        if (colonIndex >= 0)
        {
            if (colonIndex != authority.LastIndexOf(':'))
                return false;

            host = authority[..colonIndex];

            if (!IsValidPort(authority[(colonIndex + 1)..]))
                return false;
        }

        return IsValidHost(host);
    }

    private static bool IsValidPort(string port)
    {
        if (port.Length is < 1 or > 5 ||
            !port.All(char.IsAsciiDigit))
        {
            return false;
        }

        return int.TryParse(
                   port,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out var value) &&
               value is >= 1 and <= 65535;
    }

    private static bool IsValidHost(string host)
    {
        if (string.IsNullOrEmpty(host))
            return false;

        string asciiHost;

        try
        {
            var idn = new IdnMapping
            {
                UseStd3AsciiRules = true
            };

            asciiHost = idn.GetAscii(host);
        }
        catch (ArgumentException)
        {
            return false;
        }

        // Permit one trailing dot for a fully qualified domain name.
        if (asciiHost.EndsWith('.'))
            asciiHost = asciiHost[..^1];

        if (string.IsNullOrEmpty(asciiHost) ||
            asciiHost.EndsWith('.'))
        {
            return false;
        }

        if (asciiHost.Equals(
                "localhost",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Accept only unambiguous, four-part dotted-decimal IPv4 addresses.
        if (asciiHost.All(c => char.IsAsciiDigit(c) || c == '.'))
            return IsValidIpv4(asciiHost);

        if (asciiHost.Length > 253)
            return false;

        var labels = asciiHost.Split('.');

        // Permit valid single-label hostnames used by routers, NAS devices, and other LAN services.
        if (labels.Length == 1)
            return IsValidDnsLabel(labels[0]);

        // Avoid browser-specific interpretation of numeric final labels.
        if (IsPotentialIpv4Number(labels[^1]))
            return false;

        return labels.All(IsValidDnsLabel);
    }

    private static bool IsValidDnsLabel(string label)
    {
        return label.Length is > 0 and <= 63 &&
               char.IsAsciiLetterOrDigit(label[0]) &&
               char.IsAsciiLetterOrDigit(label[^1]) &&
               label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }

    private static bool IsPotentialIpv4Number(string label)
    {
        if (label.All(char.IsAsciiDigit))
            return true;

        return label.Length > 2 &&
               label[0] == '0' &&
               label[1] is 'x' or 'X' &&
               label[2..].All(Uri.IsHexDigit);
    }

    private static bool IsValidIpv4(string address)
    {
        var parts = address.Split('.');

        if (parts.Length != 4)
            return false;

        foreach (var part in parts)
        {
            if (part.Length is < 1 or > 3 ||
                (part.Length > 1 && part[0] == '0') ||
                !part.All(char.IsAsciiDigit) ||
                !byte.TryParse(
                    part,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                return false;
            }
        }

        return true;
    }
}
