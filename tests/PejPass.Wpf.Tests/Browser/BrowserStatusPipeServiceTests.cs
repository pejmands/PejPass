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
        Assert.False(response.HelloReady);
    }

    [Fact]
    public async Task UnlockWithHello_ReturnsNotReadyWhenNoSessionCacheExists()
    {
        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session);

        var response = await service.CreateUnlockWithHelloResponseAsync(
            """{"type":"unlockWithHello","protocolVersion":1}""");

        Assert.Equal("error", response.Type);
        Assert.Equal("hello_not_ready", response.Code);
    }

    [Fact]
    public void LockVault_ReturnsLockedWhenSessionIsAlreadyLocked()
    {
        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session);

        var response = service.CreateResponse("""{"type":"lockVault","protocolVersion":1}""");

        Assert.Equal("vaultLocked", response.Type);
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
    public void EntryMatchesDomain_MatchesTheSameRegistrableDomain(string domain, bool expected)
    {
        var entry = new VaultEntry { Url = "https://example.com/login" };
        Assert.Equal(expected, BrowserStatusPipeService.EntryMatchesDomain(entry, domain));
    }

    [Theory]
    [InlineData("github.io", "attacker.github.io", false)]
    [InlineData("alice.github.io", "attacker.github.io", false)]
    [InlineData("alice.github.io", "login.alice.github.io", true)]
    [InlineData("blogspot.com", "attacker.blogspot.com", false)]
    [InlineData("alice.blogspot.com", "attacker.blogspot.com", false)]
    [InlineData("example.co.uk", "login.example.co.uk", true)]
    [InlineData("example.co.uk", "attacker.co.uk", false)]
    public void EntryMatchesDomain_RespectsPublicSuffixBoundaries(
        string entryHost, string requestedDomain, bool expected)
    {
        var entry = new VaultEntry { Url = $"https://{entryHost}/login" };
        Assert.Equal(expected, BrowserStatusPipeService.EntryMatchesDomain(entry, requestedDomain));
    }

    [Theory]
    [InlineData("github.io", null)]
    [InlineData("blogspot.com", null)]
    [InlineData("alice.github.io", "alice.github.io")]
    [InlineData("example.com", "example.com")]
    [InlineData("login.example.com", "example.com")]
    public void GetRegistrableDomain_UsesKnownPublicSuffixBoundaries(string host, string? expected)
    {
        Assert.Equal(expected, BrowserStatusPipeService.GetRegistrableDomain(host));
    }

    [Fact]
    public void GetCredentials_RebuildsIndexWhenVaultContentChanges()
    {
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        using var service = new BrowserStatusPipeService(session);
        var entry = new VaultEntry
        {
            Title = "Example",
            Username = "alice",
            Url = "https://example.com/login"
        };
        var vault = new Vault { Entries = [entry] };
        session.Open(vault, Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"), keyMaterial);

        var first = service.CreateResponse("""{"type":"getCredentials","protocolVersion":1,"domain":"example.com"}""");
        Assert.Single(first.Credentials!);

        Assert.True(vault.UpdateEntry(new VaultEntry
        {
            Id = entry.Id,
            Title = entry.Title,
            Username = entry.Username,
            Password = entry.Password,
            Url = "https://changed.example/login",
            TotpSecret = entry.TotpSecret,
            Notes = entry.Notes,
            Tags = [.. entry.Tags],
            CustomFields = [.. entry.CustomFields],
            IsFavorite = entry.IsFavorite,
            SortOrder = entry.SortOrder,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        }));

        var oldDomain = service.CreateResponse("""{"type":"getCredentials","protocolVersion":1,"domain":"example.com"}""");
        var newDomain = service.CreateResponse("""{"type":"getCredentials","protocolVersion":1,"domain":"changed.example"}""");

        Assert.Empty(oldDomain.Credentials!);
        Assert.Single(newDomain.Credentials!);
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
    public void CreateResponse_DoesNotServeFillCredentialSynchronously()
    {
        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session);

        var response = service.CreateResponse(
            """{"type":"fillCredential","protocolVersion":1,"domain":"example.com","id":"11111111-1111-1111-1111-111111111111"}""");

        Assert.Equal("error", response.Type);
        Assert.Equal("async_required", response.Code);
        Assert.Null(response.Password);
        Assert.Null(response.TotpCode);
    }

    [Fact]
    public async Task FillCredential_RequiresUnlockedVault()
    {
        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session);
        var response = await service.CreateFillCredentialResponseAsync("""{"type":"fillCredential","protocolVersion":1,"domain":"example.com","id":"11111111-1111-1111-1111-111111111111"}""");
        Assert.Equal("error", response.Type);
        Assert.Equal("vault_locked", response.Code);
        Assert.Null(response.Password);
    }

    private static readonly string[] expected =
        [
            "desktop:login.example.com:Example:alice",
            "hello:login.example.com"
        ];

    [Fact]
    public async Task FillCredential_ReturnsSecretOnlyAfterDesktopAndWindowsHelloApproval()
    {
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        var approvals = new List<string>();
        using var service = new BrowserStatusPipeService(
            session,
            (domain, title, username) =>
            {
                approvals.Add($"desktop:{domain}:{title}:{username}");
                return true;
            },
            domain =>
            {
                approvals.Add($"hello:{domain}");
                return Task.FromResult(WindowsHelloVerificationResult.Verified);
            });
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

        var response = await service.CreateFillCredentialResponseAsync(JsonSerializer.Serialize(new
        {
            type = "fillCredential",
            protocolVersion = 1,
            domain = "login.example.com",
            id = entry.Id.ToString("D")
        }));

        Assert.Equal(expected, approvals);
        Assert.Equal("credentialForFill", response.Type);
        Assert.Equal("login.example.com", response.Domain);
        Assert.Equal(entry.Id.ToString("D"), response.Id);
        Assert.Equal("alice", response.Username);
        Assert.Equal("fill-only-secret", response.Password);
        Assert.Matches("^\\d{6}$", response.TotpCode);
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", JsonSerializer.Serialize(response));
    }

    [Fact]
    public async Task FillCredential_RejectsWhenUserDeniesConfirmationWithoutInvokingWindowsHello()
    {
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        var helloCalled = false;
        using var service = new BrowserStatusPipeService(
            session,
            (_, _, _) => false,
            _ =>
            {
                helloCalled = true;
                return Task.FromResult(WindowsHelloVerificationResult.Verified);
            });
        var entry = new VaultEntry
        {
            Title = "Example",
            Username = "alice",
            Password = "must-not-be-returned",
            Url = "https://example.com/login"
        };
        session.Open(new Vault { Entries = [entry] },
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"), keyMaterial);

        var response = await service.CreateFillCredentialResponseAsync(JsonSerializer.Serialize(new
        {
            type = "fillCredential",
            protocolVersion = 1,
            domain = "login.example.com",
            id = entry.Id.ToString("D")
        }));

        Assert.False(helloCalled);
        Assert.Equal("error", response.Type);
        Assert.Equal("fill_denied", response.Code);
        Assert.Null(response.Password);
        Assert.DoesNotContain("must-not-be-returned", JsonSerializer.Serialize(response));
    }

    [Theory]
    [InlineData(WindowsHelloVerificationResult.AuthenticationFailed, "hello_failed")]
    [InlineData(WindowsHelloVerificationResult.Cancelled, "hello_cancelled")]
    [InlineData(WindowsHelloVerificationResult.Unavailable, "hello_unavailable")]
    [InlineData(WindowsHelloVerificationResult.Error, "hello_unavailable")]
    public async Task FillCredential_RejectsWhenWindowsHelloDoesNotVerify(
        WindowsHelloVerificationResult helloResult, string expectedCode)
    {
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        using var service = new BrowserStatusPipeService(
            session,
            (_, _, _) => true,
            _ => Task.FromResult(helloResult));
        var entry = new VaultEntry
        {
            Title = "Example",
            Username = "alice",
            Password = "must-not-be-returned",
            Url = "https://example.com/login"
        };
        session.Open(new Vault { Entries = [entry] },
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"), keyMaterial);

        var response = await service.CreateFillCredentialResponseAsync(JsonSerializer.Serialize(new
        {
            type = "fillCredential",
            protocolVersion = 1,
            domain = "login.example.com",
            id = entry.Id.ToString("D")
        }));

        Assert.Equal("error", response.Type);
        Assert.Equal(expectedCode, response.Code);
        Assert.Null(response.Password);
        Assert.Null(response.TotpCode);
        Assert.DoesNotContain("must-not-be-returned", JsonSerializer.Serialize(response));
    }

    [Fact]
    public async Task FillCredential_RejectsAccountFromAnotherDomain()
    {
        using var session = new VaultSession();
        using var keyMaterial = CreateMaterial();
        using var service = new BrowserStatusPipeService(
            session,
            (_, _, _) => true,
            _ => Task.FromResult(WindowsHelloVerificationResult.Verified));
        var entry = new VaultEntry
        {
            Title = "Example",
            Username = "alice",
            Password = "must-not-be-returned",
            Url = "https://example.com/login"
        };
        session.Open(new Vault { Entries = [entry] },
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass"), keyMaterial);

        var response = await service.CreateFillCredentialResponseAsync(JsonSerializer.Serialize(new
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

    [Fact]
    public void SaveCredential_RejectsUrlFromAnotherHost()
    {
        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session);
        var response = service.CreateResponse(JsonSerializer.Serialize(new
        {
            type = "saveCredential",
            protocolVersion = 1,
            domain = "example.com",
            url = "https://attacker.example.com/login",
            title = "Example",
            username = "alice",
            password = "candidate-secret"
        }));

        Assert.Equal("error", response.Type);
        Assert.Equal("invalid_request", response.Code);
        Assert.DoesNotContain("candidate-secret", JsonSerializer.Serialize(response));
    }

    [Fact]
    public void SaveCredential_RequiresUnlockedVault()
    {
        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session);
        var response = service.CreateResponse("""{"type":"saveCredential","protocolVersion":1,"domain":"example.com","url":"https://example.com/login","title":"Example","username":"alice","password":"candidate-secret"}""");

        Assert.Equal("error", response.Type);
        Assert.Equal("vault_locked", response.Code);
        Assert.Null(response.Password);
        Assert.DoesNotContain("candidate-secret", JsonSerializer.Serialize(response));
    }

    [Theory]
    [InlineData("https://example.com/login?session_id=secret#step2", "https://example.com/login")]
    [InlineData("https://example.com/login?next=%2Fhome", "https://example.com/login")]
    [InlineData("https://example.com/login", "https://example.com/login")]
    public void SanitizeCredentialUrl_RemovesQueryAndFragment(string input, string expected)
    {
        Assert.Equal(expected, BrowserStatusPipeService.SanitizeCredentialUrl(input));
    }

    [Theory]
    [InlineData("https://example.com/login/step2?token=secret", "https://example.com")]
    [InlineData("http://example.com:8080/account/login", "http://example.com:8080")]
    public void GetCredentialOrigin_RemovesPathQueryAndFragment(string input, string expected)
    {
        Assert.Equal(expected, BrowserStatusPipeService.GetCredentialOrigin(input));
    }

    [Fact]
    public async Task ReadBoundedLineAsync_RejectsOversizedMessage()
    {
        using var reader = new StringReader("12345\n");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            BrowserStatusPipeService.ReadBoundedLineAsync(reader, 4, CancellationToken.None));
    }

    [Theory]
    [InlineData("hello\n", "hello")]
    [InlineData("hello\r\n", "hello")]
    [InlineData("hello", "hello")]
    [InlineData("", null)]
    public async Task ReadBoundedLineAsync_ReadsSingleBoundedLine(string input, string? expected)
    {
        using var reader = new StringReader(input);

        var result = await BrowserStatusPipeService.ReadBoundedLineAsync(
            reader, 5, CancellationToken.None);

        Assert.Equal(expected, result);
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
