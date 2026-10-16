using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MrClean.Extensions;
using MrClean.Services;
using MrClean.Services.AvatarLogging;
using NUnit.Framework;

namespace MrClean.Tests.Services.AvatarLogging;

public class AvatarStartupTests
{
    [Test]
    public void AvatarLoggerStartsBeforeDiscordConnectionService()
    {
        var services = new ServiceCollection();
        services.AddDiscordHostedServices();
        var hostedServices = services.Where(service => service.ServiceType == typeof(IHostedService))
            .Select(service => service.ImplementationType).ToArray();

        Assert.That(hostedServices, Is.EqualTo(new[]
        {
            typeof(AvatarChangeLogger), typeof(DiscordBotService)
        }));
    }

    [Test]
    public async Task InitialSnapshotsWaitForFullMemberDownload()
    {
        var cache = new AvatarSnapshotCache();
        var download = new TaskCompletionSource<bool>();
        var members = new List<KeyValuePair<ulong, string?>> { new(1, "first") };
        var initialization = cache.InitializeAsync(() => download.Task, () => members, CancellationToken.None);

        Assert.That(cache.TryGet(1, out _), Is.False);
        members.Add(new(2, "late-member"));
        download.SetResult(true);
        await initialization;

        Assert.That(cache.TryGet(2, out var avatar), Is.True);
        Assert.That(avatar, Is.EqualTo("late-member"));
        Assert.That(cache.HasChanged(2, "changed"), Is.True);
    }

    [Test]
    public async Task FailedDownloadCanBeRetriedWithoutSeedingPartialCache()
    {
        var cache = new AvatarSnapshotCache();
        var members = new[] { new KeyValuePair<ulong, string?>(1, "avatar") };
        Assert.ThrowsAsync<InvalidOperationException>(() => cache.InitializeAsync(
            () => Task.FromException(new InvalidOperationException("download failed")),
            () => members, CancellationToken.None));
        Assert.That(cache.TryGet(1, out _), Is.False);

        await cache.InitializeAsync(() => Task.CompletedTask, () => members, CancellationToken.None);
        Assert.That(cache.TryGet(1, out _), Is.True);
    }

    [Test]
    public void CancelledDownloadDoesNotSeedSnapshots()
    {
        var cache = new AvatarSnapshotCache();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => cache.InitializeAsync(
            () => Task.CompletedTask,
            () => throw new AssertionException("Cancelled initialization read the member cache"),
            cancellation.Token));
        Assert.That(cache.TryGet(1, out _), Is.False);
    }

    [Test]
    public async Task ReconnectDownloadPreservesSnapshotsSoChangesCanBeReconciled()
    {
        var cache = new AvatarSnapshotCache();
        var members = new[] { new KeyValuePair<ulong, string?>(1, "old") };
        await cache.InitializeAsync(() => Task.CompletedTask, () => members, CancellationToken.None);
        var downloads = 0;
        members[0] = new(1, "new");
        await cache.InitializeAsync(() => { downloads++; return Task.CompletedTask; },
            () => members, CancellationToken.None);

        Assert.That(downloads, Is.EqualTo(1));
        Assert.That(cache.HasChanged(1, "new"), Is.True);
    }
}
