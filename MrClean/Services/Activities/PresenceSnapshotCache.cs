namespace MrClean.Services.Activities;

public class PresenceSnapshotCache
{
    private readonly object _gate = new();
    private readonly Dictionary<(ulong Guild, ulong User), ActivitySnapshot> _latest = new();
    private long _revision;
    private readonly Dictionary<(ulong Guild, ulong User), DateTimeOffset> _emptySince = new();
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
    public void ObserveGateway(ulong guild, ulong user, IEnumerable<ReportedActivity> activities, bool available, DateTimeOffset at)
    {
        var values = activities.ToArray();
        var confirmed = false;
        lock (_gate)
        {
            if (available && values.Length == 0)
            {
                // SocketPresence conflates absent and empty arrays. Require a second fresh online frame,
                // separated by a grace period; elapsed cache time alone never clears contributions.
                if (_emptySince.TryGetValue((guild, user), out var since)) confirmed = at - since >= TimeSpan.FromSeconds(30);
                else _emptySince[(guild, user)] = at;
            }
            else _emptySince.Remove((guild, user));
        }
        if (available && (values.Length > 0 || confirmed)) Observe(guild, user, values);
        else InvalidateMember(guild, user);
    }
    public void InvalidateMember(ulong guild, ulong user)
    {
        lock (_gate) _latest[(guild, user)] = new ActivitySnapshot(false, ++_revision, Array.Empty<ReportedActivity>());
        Changed?.Invoke(guild, user);
    }
    public void Forget(ulong guild, ulong user)
    {
        lock (_gate) { _latest.Remove((guild, user)); _emptySince.Remove((guild, user)); }
        Changed?.Invoke(guild, user);
    }
    public void Invalidate(ulong guild)
    {
        ulong[] users;
        lock (_gate)
        {
            users = _latest.Keys.Where(x => x.Guild == guild).Select(x => x.User).ToArray();
            foreach (var key in _emptySince.Keys.Where(x => x.Guild == guild).ToArray()) _emptySince.Remove(key);
            foreach (var user in users) _latest[(guild, user)] = new ActivitySnapshot(false, ++_revision, Array.Empty<ReportedActivity>());
        }
        foreach (var user in users) Changed?.Invoke(guild, user);
    }
}
