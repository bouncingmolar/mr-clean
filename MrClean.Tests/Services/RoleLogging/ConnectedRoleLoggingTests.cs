using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MrClean.Configuration;
using MrClean.Services.RoleLogging;
using NUnit.Framework;

namespace MrClean.Tests.Services.RoleLogging;

public class ConnectedRoleLoggingTests
{
    [TestCase(true, 10UL, 20UL, true)]
    [TestCase(true, 10UL, 21UL, false)]
    [TestCase(true, 11UL, 20UL, false)]
    [TestCase(false, 10UL, 20UL, false)]
    public void SuppressOnlyConfiguredConnectedRoleInManagedGuildWhenVoiceLoggingEnabled(bool enabled, ulong guild, ulong role, bool expected)
    {
        var logger = new RoleChangeLogger(null!, NullLogger<RoleChangeLogger>.Instance,
            Options.Create(new VoiceLoggingOptions { Enabled = enabled, ConnectedRoleId = 20 }),
            Options.Create(new DiscordOptions { GuildId = 10 }));
        Assert.That(logger.SuppressRole(guild, role), Is.EqualTo(expected));
    }

    [Test]
    public void UnconfiguredRoleNeverSuppressesAnything()
    {
        var logger = new RoleChangeLogger(null!, NullLogger<RoleChangeLogger>.Instance,
            Options.Create(new VoiceLoggingOptions { Enabled = true }), Options.Create(new DiscordOptions { GuildId = 10 }));
        Assert.That(logger.SuppressRole(10, 20), Is.False);
    }
}
