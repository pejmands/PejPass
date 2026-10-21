using PejPass.Wpf.Services;

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

        var response = service.CreateResponse("""{"type":"getCredentials","protocolVersion":1}""");

        Assert.Equal("error", response.Type);
        Assert.Equal("unsupported_request", response.Code);
        Assert.Null(response.Status);
    }

    [Fact]
    public void CreateResponse_RejectsMalformedJson()
    {
        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session);

        var response = service.CreateResponse("{not-json");

        Assert.Equal("error", response.Type);
        Assert.Equal("invalid_message", response.Code);
        Assert.Null(response.Status);
    }

    [Fact]
    public void CreateResponse_RejectsWrongProtocolVersion()
    {
        using var session = new VaultSession();
        using var service = new BrowserStatusPipeService(session);

        var response = service.CreateResponse("""{"type":"getVaultStatus","protocolVersion":2}""");

        Assert.Equal("error", response.Type);
        Assert.Equal("unsupported_request", response.Code);
        Assert.Null(response.Status);
    }
}
