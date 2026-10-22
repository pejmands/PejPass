using PejPass.Wpf.Records;
using PejPass.Wpf.Services;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace PejPass.Wpf.Tests.Updates;

public sealed class UpdateServiceHttpTests
{
    private const string ManifestUrl = "https://updates.example.test/update.json";
    private const string PackageUrl = "https://updates.example.test/PejPass.zip";
    private const long MaxUpdateManifestBytes = 4L * 1024 * 1024;
    private const long MaxUpdatePackageBytes = 512L * 1024 * 1024;

    [Fact]
    public async Task CheckForUpdatesAsync_UsesInjectedHttpClient()
    {
        string? requestedUri = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            requestedUri = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("offline")
            });
        }));
        using var service = new UpdateService(ManifestUrl, httpClient);

        var result = await service.CheckForUpdatesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ManifestUrl, requestedUri);
        Assert.Equal(UpdateCheckStatus.NetworkError, result.Status);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_RejectsOversizedManifestFromInjectedClient()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
        {
            var content = new ByteArrayContent([]);
            content.Headers.ContentLength = MaxUpdateManifestBytes + 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
        }));
        using var service = new UpdateService(ManifestUrl, httpClient);

        var result = await service.CheckForUpdatesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.InvalidManifest, result.Status);
        Assert.Contains("maximum supported size", result.Message);
    }

    [Fact]
    public async Task DownloadUpdateAsync_UsesInjectedHttpClientAndVerifiesPackageHash()
    {
        var payload = "test update payload"u8.ToArray();
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(payload));
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            })));
        using var service = new UpdateService(ManifestUrl, httpClient);

        string? downloadedPath = null;

        try
        {
            downloadedPath = await service.DownloadUpdateAsync(PackageUrl, expectedSha256, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(File.Exists(downloadedPath));
            Assert.Equal(payload, await File.ReadAllBytesAsync(downloadedPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (downloadedPath is not null && File.Exists(downloadedPath))
                File.Delete(downloadedPath);
        }
    }

    [Fact]
    public async Task DownloadUpdateAsync_RejectsContentLengthAbovePackageLimit()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
        {
            var content = new ByteArrayContent([]);
            content.Headers.ContentLength = MaxUpdatePackageBytes + 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
        }));
        using var service = new UpdateService(ManifestUrl, httpClient);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.DownloadUpdateAsync(PackageUrl, new string('a', 64), cancellationToken: TestContext.Current.CancellationToken));
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            sendAsync(request, cancellationToken);
    }
}
