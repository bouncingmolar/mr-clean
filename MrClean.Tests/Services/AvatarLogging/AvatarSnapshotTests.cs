using MrClean.Services.AvatarLogging;
using NUnit.Framework;

namespace MrClean.Tests.Services.AvatarLogging;

public class AvatarSnapshotTests
{
    [Test]
    public void GuildAvatarTakesPrecedenceOverGlobalAvatar()
    {
        Assert.That(
            AvatarSnapshot.GetEffectiveAvatarId("guild-avatar", "global-avatar"),
            Is.EqualTo("guild-avatar"));
    }

    [Test]
    public void GlobalAvatarChangeDoesNotChangeEffectiveAvatarWhenGuildOverrideExists()
    {
        var before = AvatarSnapshot.GetEffectiveAvatarId("guild-avatar", "old-global");
        var after = AvatarSnapshot.GetEffectiveAvatarId("guild-avatar", "new-global");

        Assert.That(after, Is.EqualTo(before));
    }

    [Test]
    public void GlobalAvatarChangeChangesEffectiveAvatarWithoutGuildOverride()
    {
        var before = AvatarSnapshot.GetEffectiveAvatarId(null, "old-global");
        var after = AvatarSnapshot.GetEffectiveAvatarId(null, "new-global");

        Assert.That(after, Is.Not.EqualTo(before));
    }

    [Test]
    public void RememberedAvatarSuppressesDuplicateChanges()
    {
        var cache = new AvatarSnapshotCache();
        cache.Remember(42, "initial-avatar");

        Assert.That(cache.HasChanged(42, "new-avatar"), Is.True);

        cache.Remember(42, "new-avatar");

        Assert.That(cache.HasChanged(42, "new-avatar"), Is.False);
    }

    [Test]
    public void FirstAvatarSnapshotCanBeSeededWithoutAChange()
    {
        var cache = new AvatarSnapshotCache();

        Assert.That(cache.HasChanged(42, "existing-avatar"), Is.False);

        cache.Remember(42, "existing-avatar");

        Assert.That(cache.HasChanged(42, "existing-avatar"), Is.False);
    }
}
