using MrClean.Models;

namespace MrClean.Services.VoiceLogging;

public record VoiceOccupant(ulong User, ulong Channel, string Name);

public static class VoiceRuleObservation
{
    // Called by the single event processor using an immutable cache snapshot, never by REST delivery.
    public static void Reconcile(VoiceActivityTracker state, IReadOnlyList<VoiceOccupant> occupants,
        IReadOnlyDictionary<ulong, VoiceChannelRule> rules, DateTimeOffset at, TimeSpan window, ulong fallback, IReadOnlyDictionary<ulong, string>? names = null)
    {
        foreach (var session in state.Sessions.Where(x => x.ConnectedSince != null).ToArray())
        {
            var occupant = occupants.FirstOrDefault(x => x.User == session.UserId);
            rules.TryGetValue(session.ChannelId, out var oldRule);
            if (occupant?.Channel == session.ChannelId && oldRule?.Enabled == true) continue;
            session.Rule ??= new VoiceChannelRule();
            session.Rule.LogDeparture = oldRule?.Enabled == true && oldRule.LogDeparture;
            // A missed event has an unknown departure time: count only the last confirmed checkpoint.
            state.Transition(session.UserId, null, null, at, window, fallback,
                uncertain: occupant?.Channel != session.ChannelId,
                activityAtEnd: id => oldRule?.DynamicName == true && names?.TryGetValue(id, out var name) == true ? oldRule.Activity(name) : null);
        }
        foreach (var occupant in occupants)
        {
            if (!rules.TryGetValue(occupant.Channel, out var rule) || !rule.Enabled) continue;
            state.Transition(occupant.User, occupant.Channel, rule.Activity(occupant.Name), at, window, rule.DestinationId, rule: rule);
        }
        state.Checkpoint(at, window);
    }
}
