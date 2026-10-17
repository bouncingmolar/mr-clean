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
            // Presentation and retry metadata live in the existing text column; legacy rows remain readable.
            return new VoiceLogMessage { Id = id, Destination = log.Destination, Text = new VoiceMessagePayload { Content = log.EmbedOnly ? null : log.Text, Description = log.EmbedOnly ? log.Text : null }.Serialize() };
        }).ToArray();
        state.Pending.Clear();
        return new VoiceStateCommit(JsonSerializer.Serialize(state), logs);
    }
}
