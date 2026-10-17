using System.Threading.Channels;

namespace MrClean.Services.VoiceLogging;

// Capture immutable gateway observations synchronously; one reader preserves their order.
public sealed class VoiceEventPump
{
    private readonly Channel<Action<VoiceActivityTracker>> _events = Channel.CreateUnbounded<Action<VoiceActivityTracker>>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public void Capture(Action<VoiceActivityTracker> observation)
    {
        if (!_events.Writer.TryWrite(observation)) throw new InvalidOperationException("Voice event queue closed.");
    }

    public void Complete() => _events.Writer.TryComplete();

    public async Task RunAsync(VoiceActivityTracker state, Func<Task> persist, CancellationToken cancellationToken)
    {
        await foreach (var observation in _events.Reader.ReadAllAsync(cancellationToken))
        {
            observation(state);
            // Retrying the commit must not apply the observation a second time.
            await persist();
        }
    }
}
