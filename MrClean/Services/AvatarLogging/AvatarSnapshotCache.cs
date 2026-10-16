namespace MrClean.Services.AvatarLogging;

public sealed class AvatarSnapshotCache
{
    private readonly Dictionary<ulong, string?> _avatars = new();

    private bool _initialized;

    public async Task InitializeAsync(
        Func<Task> downloadMembers,
        Func<IEnumerable<KeyValuePair<ulong, string?>>> getSnapshots,
        CancellationToken cancellationToken)
    {
        // Downloads must complete before reading the cache. Failure leaves initialization
        // retryable; reconnects refresh members without overwriting existing snapshots.
        await downloadMembers();
        cancellationToken.ThrowIfCancellationRequested();
        if (_initialized)
            return;

        var snapshots = getSnapshots().ToArray();
        foreach (var snapshot in snapshots)
            Remember(snapshot.Key, snapshot.Value);
        _initialized = true;
    }

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
