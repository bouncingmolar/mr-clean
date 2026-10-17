using MrClean.Models;

namespace MrClean.Services.VoiceLogging;

public static class VoiceLogDelivery
{
    // The intent is durable before sending. On an uncertain retry, reconcile history first.
    public static async Task DeliverAsync(VoiceLogMessage log, Func<Task<bool>> alreadyDelivered,
        Func<Task> saveIntent, Func<Task> send, Func<Task> acknowledge)
    {
        if (log.AttemptedAt != null && await alreadyDelivered())
        {
            await acknowledge();
            return;
        }
        log.AttemptedAt ??= DateTimeOffset.UtcNow;
        await saveIntent();
        await send();
        await acknowledge();
    }
}
