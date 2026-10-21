using PejPass.Application.Security;
using PejPass.Domain.Entities;
using PejPass.Wpf.Services;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace PejPass.Wpf.Tests.Browser;

public sealed class BrowserStatusPipeServiceTests
{
    [Fact]
    public void GetVaultStatus_ReturnsLockedWhenNoVaultSessionIsActive()
    {
        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session);
        var response = service.CreateResponse("""{"type":"getVaultStatus","protocolVersion":1}""");
        Assert.Equal("vaultStatus", response.Type);
        Assert.Equal("PejPass", response.App);
        Assert.Equal(1, response.ProtocolVersion);
        Assert.Equal("locked", response.Status);
        Assert.Null(response.Code);
    }

    [Fact]
    public void CreateResponse_RejectsUnsupportedRequest()
    {
        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session);
        var response = service.CreateResponse("""{"type":"deleteCredentials","protocolVersion":1}""");
        Assert.Equal("error", response.Type);
        Assert.Equal("unsupported_request", response.Code);
        Assert.Null(response.Credentials);
    }

    [Fact]
    public void CreateResponse_RejectsMalformedJson()
    {
        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session);
        var response = service.CreateResponse("{not-json");
        Assert.Equal("error", response.Type);
        Assert.Equal("invalid_message", response.Code);
    }

    [Fact]
    public void CreateResponse_RejectsWrongProtocolVersion()
    {
        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session);
        var response = service.CreateResponse("""{"type":"getVaultStatus","protocolVersion":2}""");
        Assert.Equal("error", response.Type);
        Assert.Equal("unsupported_request", response.Code);
    }

    [Fact]
    public void GetCredentials_RequiresUnlockedVault()
    {
        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session);
        var response = service.CreateResponse("""{"type":"getCredentials","protocolVersion":1,"domain":"example.com"}""");
        Assert.Equal("error", response.Type);
        Assert.Equal("vault_locked", response.Code);
        Assert.Null(response.Credentials);
    }

    [Theory]
    [InlineData("example.com", true)]
    [InlineData("login.example.com", true)]
    [InlineData("example.com.attacker.test", false)]
    [InlineData("notexample.com", false)]
    public void EntryMatchesDomain_OnlyMatchesExactHostOrItsSubdomains(string domain, bool expected)
    {
        var entry = new VaultEntry { Url = "https://example.com/login" };
        Assert.Equal(expected, BrowserStatusPipeService.EntryMatchesDomain(entry, domain));
    }

    [Fact]
    public void GetCredentials_ReturnsMatchingMetadataWithoutSecrets()
    {
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        using var service = new BrowserStatusPipeService(session);
        var vault = new Vault
        {
            Entries =
            [
                new VaultEntry { Title = "Example", Username = "alice", Password = "must-not-leave-desktop", Url = "https://example.com/login" },
                new VaultEntry { Title = "Other site", Username = "bob", Password = "unrelated-secret", Url = "https://elsewhere.test" }
            ]
        };
        session.Open(vault, Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"), keyMaterial);

        var response = service.CreateResponse("""{"type":"getCredentials","protocolVersion":1,"domain":"login.example.com"}""");

        Assert.Equal("credentialMatches", response.Type);
        Assert.Equal("login.example.com", response.Domain);
        var credential = Assert.Single(response.Credentials!);
        Assert.Equal("Example", credential.Title);
        Assert.Equal("alice", credential.Username);
        Assert.Equal("https://example.com/login", credential.Url);
        Assert.DoesNotContain("must-not-leave-desktop", JsonSerializer.Serialize(response));
        Assert.DoesNotContain("unrelated-secret", JsonSerializer.Serialize(response));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("example.com/path")]
    [InlineData("example.com:444")]
    [InlineData("user@example.com")]
    [InlineData("")]
    public void GetCredentials_RejectsInvalidDomain(string domain)
    {
        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session);
        var response = service.CreateResponse(JsonSerializer.Serialize(new { type = "getCredentials", protocolVersion = 1, domain }));
        Assert.Equal("error", response.Type);
        Assert.Equal("invalid_domain", response.Code);
    }

    [Fact]
    public void FillCredential_RequiresUnlockedVault()
    {
        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session);
        var response = service.CreateResponse("""{"type":"fillCredential","protocolVersion":1,"domain":"example.com","id":"11111111-1111-1111-1111-111111111111"}""");
        Assert.Equal("error", response.Type);
        Assert.Equal("vault_locked", response.Code);
        Assert.Null(response.Password);
    }

    [Fact]
    public void FillCredential_ReturnsSecretOnlyForMatchingAccountAndDomain()
    {
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        using var service = new BrowserStatusPipeService(session);
        var entry = new VaultEntry
        {
            Title = "Example",
            Username = "alice",
            Password = "fill-only-secret",
            TotpSecret = "JBSWY3DPEHPK3PXP",
            Url = "https://example.com/login"
        };
        var vault = new Vault { Entries = [entry] };
        session.Open(vault, Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"), keyMaterial);

        var response = service.CreateResponse(JsonSerializer.Serialize(new
        {
            type = "fillCredential",
            protocolVersion = 1,
            domain = "login.example.com",
            id = entry.Id.ToString("D")
        }));

        Assert.Equal("credentialForFill", response.Type);
        Assert.Equal("login.example.com", response.Domain);
        Assert.Equal(entry.Id.ToString("D"), response.Id);
        Assert.Equal("alice", response.Username);
        Assert.Equal("fill-only-secret", response.Password);
        Assert.Matches("^\\d{6}$", response.TotpCode);
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", JsonSerializer.Serialize(response));
    }

    [Fact]
    public void FillCredential_RejectsAccountFromAnotherDomain()
    {
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        using var service = new BrowserStatusPipeService(session);
        var entry = new VaultEntry
        {
            Title = "Example",
            Username = "alice",
            Password = "must-not-be-returned",
            Url = "https://example.com/login"
        };
        session.Open(new Vault { Entries = [entry] },
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"), keyMaterial);

        var response = service.CreateResponse(JsonSerializer.Serialize(new
        {
            type = "fillCredential",
            protocolVersion = 1,
            domain = "attacker.test",
            id = entry.Id.ToString("D")
        }));

        Assert.Equal("error", response.Type);
        Assert.Equal("credential_not_found", response.Code);
        Assert.Null(response.Password);
    }

    private static VaultKeyMaterial CreateMaterial()
    {
        var key = new byte[32];
        var salt = new byte[16];
        RandomNumberGenerator.Fill(key);
        RandomNumberGenerator.Fill(salt);
        try { return new VaultKeyMaterial(key, salt, Argon2Parameters.Default); }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(salt);
        }
    }
}
