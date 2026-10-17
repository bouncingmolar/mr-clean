using System.Text.Json;
using MrClean.Models;

namespace MrClean.Services.VoiceLogging;

public record VoiceStateCommit(string Json, VoiceLogMessage[] Logs)
{
    public static VoiceStateCommit Prepare(VoiceActivityTracker state)
    {
        var logs = state.Pending.Select(log =>
        {
            var id = ++state.LastLogId;
            // Stable visible reference distinguishes otherwise identical rapid events during retry reconciliation.
            return new VoiceLogMessage { Id = id, Destination = log.Destination, Text = $"{log.Text} [vc:{id}]" };
        }).ToArray();
        state.Pending.Clear();
        return new VoiceStateCommit(JsonSerializer.Serialize(state), logs);
    }
}
