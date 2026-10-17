using System;
using Microsoft.Extensions.Configuration;
using MrClean.Configuration;
using NUnit.Framework;
namespace MrClean.Tests.Services.VoiceLogging;
public class VoiceServerConfigurationTests
{
    [Test] public void GlobalSettingsPreserveGraceAndConnectedProtection()
    {
        var config = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json").Build();
        var options = config.GetSection("VoiceLogging").Get<VoiceLoggingOptions>();
        Assert.That(options.ReconnectWindowSeconds, Is.EqualTo(120));
        Assert.That(options.ConnectedRoleId, Is.EqualTo(894787914599370803UL));
        Assert.That(options.Enabled, Is.False);
        Assert.That(config.GetSection("VoiceLogging:Channels").Exists(), Is.False);
    }
}
