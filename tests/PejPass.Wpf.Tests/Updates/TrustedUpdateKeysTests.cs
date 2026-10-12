using PejPass.Application.Security;
using System.Security.Cryptography;

namespace PejPass.Wpf.Tests.Updates;

public sealed class TrustedUpdateKeysTests
{
    [Fact]
    public void ListHasBetweenOneAndThreeKeys()
    {
        Assert.InRange(TrustedUpdateKeys.PublicKeysBase64.Count, 1, 3);
    }

    [Fact]
    public void EveryEmbeddedKeyIsAValidDistinctP256Key()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var text in TrustedUpdateKeys.PublicKeysBase64)
        {
            var bytes = Convert.FromBase64String(text);

            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(bytes, out var read);

            Assert.Equal(bytes.Length, read);
            Assert.Equal(256, key.KeySize);
            Assert.True(seen.Add(text), "Duplicate trusted update key.");
        }
    }
}
