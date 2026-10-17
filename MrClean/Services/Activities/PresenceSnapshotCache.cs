namespace MrClean.Services.Activities;

public class PresenceSnapshotCache
{
    private readonly object _gate = new();
    private readonly Dictionary<(ulong Guild, ulong User), ActivitySnapshot> _latest = new();
    private long _revision;
    public event Action<ulong, ulong>? Changed;
    public ActivitySnapshot Get(ulong guild, ulong user)
    {
        lock (_gate) return _latest.GetValueOrDefault((guild, user)) ?? new ActivitySnapshot(false, 0, Array.Empty<ReportedActivity>());
    }
    public ulong[] Users(ulong guild) { lock (_gate) return _latest.Keys.Where(x => x.Guild == guild).Select(x => x.User).ToArray(); }
    public void Observe(ulong guild, ulong user, IEnumerable<ReportedActivity> activities, long? sourceRevision = null)
    {
        lock (_gate)
        {
            var revision = sourceRevision ?? ++_revision;
            _revision = Math.Max(_revision, revision);
            if (_latest.TryGetValue((guild, user), out var before) && before.Revision >= revision) return;
            _latest[(guild, user)] = new ActivitySnapshot(true, revision, activities.Distinct().ToArray());
        }
        Changed?.Invoke(guild, user);
    }
    public void SeedPositive(ulong guild, ulong user, IEnumerable<ReportedActivity> activities)
    {
        lock (_gate)
        {
            if (_latest.TryGetValue((guild, user), out var previous) && previous.Known) return;
            _latest[(guild, user)] = new ActivitySnapshot(false, ++_revision, activities.ToArray());
        }
        Changed?.Invoke(guild, user);
    }
    // Only call for a fresh PresenceUpdated event while the guild/gateway is available.
    // Discord specifies activities as the current list, including [] on activity termination.
    // Startup caches and unavailable connections have different provenance: invalidate/seed them instead.
    public void ObserveGateway(ulong guild, ulong user, IEnumerable<ReportedActivity> activities)
        => Observe(guild, user, activities);
    public void InvalidateMember(ulong guild, ulong user)
    {
        lock (_gate) _latest[(guild, user)] = new ActivitySnapshot(false, ++_revision, Array.Empty<ReportedActivity>());
        Changed?.Invoke(guild, user);
    }
    public void Forget(ulong guild, ulong user)
    {
        lock (_gate) _latest.Remove((guild, user));
        Changed?.Invoke(guild, user);
    }
    public void Invalidate(ulong guild)
    {
        ulong[] users;
        lock (_gate)
        {
            users = _latest.Keys.Where(x => x.Guild == guild).Select(x => x.User).ToArray();
            foreach (var user in users) _latest[(guild, user)] = new ActivitySnapshot(false, ++_revision, Array.Empty<ReportedActivity>());
        }
        foreach (var user in users) Changed?.Invoke(guild, user);
    }
}
