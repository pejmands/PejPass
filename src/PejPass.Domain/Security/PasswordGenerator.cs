using System.Security.Cryptography;
using System.Text;

namespace PejPass.Domain.Security;

public static class PasswordGenerator
{
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{};:,.?";

    private const string Ambiguous = "0Oo1lI|`";

    public static string Generate(PasswordGeneratorOptions? options = null)
    {
        options ??= new PasswordGeneratorOptions();

        var length = Math.Clamp(options.Length, PasswordGeneratorOptions.MinLength, PasswordGeneratorOptions.MaxLength);

        var pools = new List<string>();
        if (options.IncludeLowercase) pools.Add(Filter(Lower, options.ExcludeAmbiguous));
        if (options.IncludeUppercase) pools.Add(Filter(Upper, options.ExcludeAmbiguous));
        if (options.IncludeDigits) pools.Add(Filter(Digits, options.ExcludeAmbiguous));
        if (options.IncludeSymbols) pools.Add(Filter(Symbols, options.ExcludeAmbiguous));

        pools.RemoveAll(string.IsNullOrEmpty);

        if (pools.Count == 0)
            throw new InvalidOperationException("Select at least one character set.");

        // Ensure at least one char from each selected pool
        var chars = new char[length];
        var index = 0;
        foreach (var pool in pools)
        {
            if (index >= length) break;
            chars[index++] = pool[RandomInt(pool.Length)];
        }

        var all = string.Concat(pools);
        while (index < length)
            chars[index++] = all[RandomInt(all.Length)];

        // Fisher–Yates shuffle
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomInt(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    private static string Filter(string source, bool excludeAmbiguous)
    {
        if (!excludeAmbiguous) return source;
        var sb = new StringBuilder(source.Length);
        foreach (var c in source)
        {
            if (Ambiguous.IndexOf(c) < 0)
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static int RandomInt(int maxExclusive)
    {
        return RandomNumberGenerator.GetInt32(maxExclusive);
    }
}
