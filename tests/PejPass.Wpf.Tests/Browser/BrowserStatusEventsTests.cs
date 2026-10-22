using PejPass.Application.Security;
using PejPass.Domain.Entities;
using PejPass.Wpf.Services;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;

namespace PejPass.Wpf.Tests.Browser;

public sealed class BrowserStatusEventsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact(Timeout = 15000)]
    public async Task NewSubscriber_WhenLocked_ReceivesLockedSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = new VaultSession();
        using var service = StartService(session, out var pipe);
        await using var client = await EventClient.ConnectAsync(pipe, ct);

        Assert.Equal("locked", await client.ReadStatusAsync(ct));
    }

    [Fact(Timeout = 15000)]
    public async Task NewSubscriber_WhenAlreadyUnlocked_ReceivesUnlockedSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = new VaultSession();
        using var material = CreateMaterial();
        session.Open(new Vault(), TempVaultPath(), material);

        using var service = StartService(session, out var pipe);
        await using var client = await EventClient.ConnectAsync(pipe, ct);

        Assert.Equal("unlocked", await client.ReadStatusAsync(ct));
    }

    [Fact(Timeout = 15000)]
    public async Task OpenAndClear_PublishUnlockedThenLocked()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = new VaultSession();
        using var material = CreateMaterial();
        using var service = StartService(session, out var pipe);
        await using var client = await EventClient.ConnectAsync(pipe, ct);

        Assert.Equal("locked", await client.ReadStatusAsync(ct));

        session.Open(new Vault(), TempVaultPath(), material);
        Assert.Equal("unlocked", await client.ReadStatusAsync(ct));

        session.Clear();
        Assert.Equal("locked", await client.ReadStatusAsync(ct));
    }

    [Fact(Timeout = 15000)]
    public async Task KeyMaterialUpdate_DoesNotRepublishSameStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = new VaultSession();
        using var material = CreateMaterial();
        using var replacement = CreateMaterial();
        using var service = StartService(session, out var pipe);
        await using var client = await EventClient.ConnectAsync(pipe, ct);

        Assert.Equal("locked", await client.ReadStatusAsync(ct));
        session.Open(new Vault(), TempVaultPath(), material);
        Assert.Equal("unlocked", await client.ReadStatusAsync(ct));

        Assert.True(session.TryUpdateKeyMaterial(replacement));
        session.Clear();

        Assert.Equal("locked", await client.ReadStatusAsync(ct));
    }

    [Fact(Timeout = 15000)]
    public async Task AllSubscribers_ReceiveEveryEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = new VaultSession();
        using var material = CreateMaterial();
        using var service = StartService(session, out var pipe);
        await using var first = await EventClient.ConnectAsync(pipe, ct);
        await using var second = await EventClient.ConnectAsync(pipe, ct);

        Assert.Equal("locked", await first.ReadStatusAsync(ct));
        Assert.Equal("locked", await second.ReadStatusAsync(ct));

        session.Open(new Vault(), TempVaultPath(), material);

        Assert.Equal("unlocked", await first.ReadStatusAsync(ct));
        Assert.Equal("unlocked", await second.ReadStatusAsync(ct));
    }

    [Fact(Timeout = 15000)]
    public async Task DisconnectedSubscriber_IsRemoved()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = new VaultSession();
        using var service = StartService(session, out var pipe);

        var client = await EventClient.ConnectAsync(pipe, ct);
        Assert.Equal("locked", await client.ReadStatusAsync(ct));
        await WaitUntilAsync(() => service.SubscriberCount == 1, ct);

        await client.DisposeAsync();
        await WaitUntilAsync(() => service.SubscriberCount == 0, ct);
    }

    [Fact(Timeout = 15000)]
    public async Task SubscriberBeyondLimit_IsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        using var session = new VaultSession();
        using var service = StartService(session, out var pipe);

        var accepted = new List<EventClient>();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                var client = await EventClient.ConnectAsync(pipe, ct);
                accepted.Add(client);
                Assert.Equal("locked", await client.ReadStatusAsync(ct));
            }

            await using var extra = await EventClient.ConnectAsync(pipe, ct);
            Assert.Null(await extra.ReadStatusAsync(ct));
            Assert.Equal(4, service.SubscriberCount);
        }
        finally
        {
            foreach (var client in accepted)
                await client.DisposeAsync();
        }
    }

    private static BrowserStatusPipeService StartService(VaultSession session, out string pipeName)
    {
        pipeName = $"PejPass.Tests.Events.{Guid.NewGuid():N}";
        var service = new BrowserStatusPipeService(session, null, pipeName);
        service.StartEventsOnly();
        return service;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("Condition was not met in time.");
            await Task.Delay(20, ct);
        }
    }

    private static string TempVaultPath() =>
        Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pejpass");

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

    private sealed class EventClient : IAsyncDisposable
    {
        private readonly NamedPipeClientStream _pipe;
        private readonly StreamReader _reader;

        private EventClient(NamedPipeClientStream pipe)
        {
            _pipe = pipe;
            _reader = new StreamReader(pipe);
        }

        public static async Task<EventClient> ConnectAsync(string pipeName, CancellationToken ct)
        {
            var pipe = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            await pipe.ConnectAsync((int)Timeout.TotalMilliseconds, ct);
            return new EventClient(pipe);
        }

        public async Task<string?> ReadStatusAsync(CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);

            var line = await _reader.ReadLineAsync(cts.Token);
            if (line is null) return null;

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            Assert.Equal("vaultStateChanged", root.GetProperty("type").GetString());
            Assert.Equal("PejPass", root.GetProperty("app").GetString());
            Assert.Equal(1, root.GetProperty("protocolVersion").GetInt32());
            return root.GetProperty("status").GetString();
        }

        public async ValueTask DisposeAsync()
        {
            _reader.Dispose();
            await _pipe.DisposeAsync();
        }
    }
}
