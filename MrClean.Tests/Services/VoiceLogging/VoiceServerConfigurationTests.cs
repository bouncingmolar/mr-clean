using System;
using Microsoft.Extensions.Configuration;
using MrClean.Configuration;
using NUnit.Framework;

namespace MrClean.Tests.Services.VoiceLogging;

public class VoiceServerConfigurationTests
{
    [Test]
    public void ServerConfigurationBindsExactSourceIdsAndRoutesWhileDisabled()
    {
        var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json").Build();
        var options = configuration.GetSection("VoiceLogging").Get<VoiceLoggingOptions>();
        Assert.That(configuration.GetValue<ulong>("Discord:GuildId"), Is.EqualTo(683633975838769192UL));
        Assert.That(options.Enabled, Is.False);
        Assert.That(options.GeneralChannelId, Is.EqualTo(798528429091061780UL));
        Assert.That(options.ReconnectWindowSeconds, Is.EqualTo(120));
        Assert.That(options.LogChannelId, Is.EqualTo(806802061693550652UL));
        Assert.That(options.Channels.Count, Is.EqualTo(3));
        Assert.That(options.ChannelLogDestinations.Count, Is.EqualTo(4));
        Assert.That(options.Activity(798528429091061780, "Among Us"), Is.EqualTo("Among Us"));
        Assert.That(options.Destination(798528429091061780), Is.EqualTo(806802061693550652UL));
        Assert.That(options.Activity(792285032713945119, "Renamed"), Is.EqualTo("Music"));
        Assert.That(options.Destination(792285032713945119), Is.EqualTo(943558876706533477UL));
        Assert.That(options.Activity(1126920146099912744, "Renamed"), Is.EqualTo("Gartic Phone"));
        Assert.That(options.Destination(1126920146099912744), Is.EqualTo(1556935771049689119UL));
        Assert.That(options.Activity(1126902663607484476, "Renamed"), Is.EqualTo("Codenames"));
        Assert.That(options.Destination(1126902663607484476), Is.EqualTo(1556935771049689119UL));
        Assert.That(options.Activity(1556935771049689119, "Text destination"), Is.Null);
    }
}
