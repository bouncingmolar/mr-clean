using Discord;
using MrClean.Models;

namespace MrClean.Services.Activities;

public record ReportedActivity(string Name, ActivityType Type, ulong? ApplicationId = null);
public record ActivitySnapshot(bool Known, long Revision, IReadOnlyList<ReportedActivity> Activities);

public static class ActivityMatching
{
    public static bool Supported(ActivityType type) => type is ActivityType.Playing or ActivityType.Listening or ActivityType.Watching or ActivityType.Streaming;
    public static HashSet<ulong> Roles(IEnumerable<ReportedActivity> activities, IEnumerable<ActivityRule> rules, IEnumerable<ActivityFallback> fallbacks)
    {
        var tracked = rules.Where(x => x.Enabled).ToArray();
        var generic = fallbacks.Where(x => x.Enabled).ToArray();
        var result = new HashSet<ulong>();
        foreach (var activity in activities.Where(x => Supported(x.Type)))
        {
            var matches = tracked.Where(x => x.Type == activity.Type &&
                string.Equals(x.Name, activity.Name, StringComparison.Ordinal) &&
                (x.ApplicationId == null || x.ApplicationId == activity.ApplicationId)).ToArray();
            if (matches.Length > 0) result.UnionWith(matches.Select(x => x.RoleId));
            else result.UnionWith(generic.Where(x => x.Type == activity.Type).Select(x => x.RoleId));
        }
        return result;
    }
}
