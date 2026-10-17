namespace MrClean.Configuration;

public class ActivityOptions
{
    // Requests the privileged intent and enables capture/diagnostics; guild management is a separate DB switch.
    public bool ObservePresences { get; set; }
    public int ReconcileSeconds { get; set; } = 30;
}
