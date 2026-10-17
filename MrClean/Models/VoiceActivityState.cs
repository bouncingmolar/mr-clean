namespace MrClean.Models;

// A single durable state document contains sessions and their ordered delivery queue.
public class VoiceActivityState
{
    public int Id { get; set; }
    public string Json { get; set; } = "{}";
}
