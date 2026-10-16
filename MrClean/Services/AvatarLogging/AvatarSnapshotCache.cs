namespace MrClean.Services.AvatarLogging;

public sealed class AvatarSnapshotCache
{
    private readonly Dictionary<ulong, string?> _avatars = new();

    public void Remember(ulong userId, string? effectiveAvatarId)
    {
        _avatars[userId] = effectiveAvatarId;
    }

    public bool TryGet(ulong userId, out string? effectiveAvatarId)
    {
        return _avatars.TryGetValue(userId, out effectiveAvatarId);
    }

    public bool HasChanged(ulong userId, string? effectiveAvatarId)
    {
        return _avatars.TryGetValue(userId, out var previousAvatarId) &&
               !string.Equals(previousAvatarId, effectiveAvatarId, StringComparison.Ordinal);
    }
}

public static class AvatarSnapshot
{
    public static string? GetEffectiveAvatarId(string? guildAvatarId, string? avatarId)
    {
        return guildAvatarId ?? avatarId;
    }
}
