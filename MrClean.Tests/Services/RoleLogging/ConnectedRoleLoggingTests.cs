using System.Linq;
using MrClean.Services.RoleLogging;
using NUnit.Framework;

namespace MrClean.Tests.Services.RoleLogging;

public class ConnectedRoleLoggingTests
{
    [Test]
    public void ConnectedRoleIsNoLongerSpecialCasedInRoleLogger()
    {
        // All roles use the same existing configured-route lookup.
        var routes = new[] { new MrClean.Models.RoleLogRoute { RoleId = 894787914599370803, ChannelId = 1556935145418919937 } };
        Assert.That(RoleChangeLogger.Destination(894787914599370803, routes, 1), Is.EqualTo(1556935145418919937UL));
        Assert.That(RoleChangeLogger.Destination(2, routes, 1), Is.EqualTo(1UL));
    }
}
