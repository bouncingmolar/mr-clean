using Discord;
using Discord.WebSocket;

namespace MrClean.Commands.Administration;

// Small transport boundary keeps Discord's deferred-original visibility rules testable.
internal interface IAdminCommandInteraction
{
    Task RejectAsync(string text);
    Task DeferAsync(bool ephemeral);
    Task CompleteAsync(string text);
    Task FollowupAsync(string text, bool ephemeral);
}

internal sealed class SocketAdminCommandInteraction : IAdminCommandInteraction
{
    private readonly SocketSlashCommand _command;
    public SocketAdminCommandInteraction(SocketSlashCommand command) => _command = command;
    public Task RejectAsync(string text) => _command.RespondAsync(text, ephemeral: true, allowedMentions: AllowedMentions.None);
    public Task DeferAsync(bool ephemeral) => _command.DeferAsync(ephemeral: ephemeral);
    public async Task CompleteAsync(string text) => await _command.ModifyOriginalResponseAsync(message =>
    { message.Content = text; message.AllowedMentions = AllowedMentions.None; });
    public async Task FollowupAsync(string text, bool ephemeral) =>
        await _command.FollowupAsync(text, ephemeral: ephemeral, allowedMentions: AllowedMentions.None);
}

internal sealed class AdminCommandResponse
{
    private readonly IAdminCommandInteraction _interaction;
    private readonly bool _ephemeral;
    private readonly ILogger _logger;
    private AdminCommandResponse(IAdminCommandInteraction interaction, bool ephemeral, ILogger logger)
    { _interaction = interaction; _ephemeral = ephemeral; _logger = logger; }

    internal static bool IsPrivate(string root, string action) => root == "activity" && action == "inspect";
    internal static async Task<AdminCommandResponse?> StartAsync(IAdminCommandInteraction interaction,
        string root, string action, bool authorized, ILogger logger)
    {
        // Reject privately before any public acknowledgement, database read or mutation.
        if (!authorized)
        {
            await interaction.RejectAsync("Manage Server or Administrator permission in the configured server is required.");
            return null;
        }
        var ephemeral = IsPrivate(root, action);
        await interaction.DeferAsync(ephemeral);
        return new AdminCommandResponse(interaction, ephemeral, logger);
    }

    internal async Task ExecuteAsync(Func<Task<string>> operation)
    {
        string text;
        try { text = await operation(); }
        catch (Exception ex)
        {
            // Do not log activity names/member diagnostics or send internal exception details publicly.
            _logger.LogWarning("Administration request failed ({ExceptionType}).", ex.GetType().Name);
            var detail = ex is ArgumentException or UnauthorizedAccessException ? ex.Message :
                "Unable to complete the request. Review current settings before retrying; some changes may already have committed.";
            if (_ephemeral) await SendAsync(detail);
            else
            {
                // Resolve the deferred original explicitly before a separate private followup.
                // A first followup used to resolve a defer inherits the original visibility.
                await _interaction.CompleteAsync("Request did not complete. Review current settings before retrying; some changes may already have committed. Details were sent privately.");
                await _interaction.FollowupAsync(detail, true);
            }
            return;
        }
        // A delivery failure is not an operation failure: do not falsely announce rollback or retry it.
        await SendAsync(text);
    }

    private async Task SendAsync(string text)
    {
        var chunks = text.Chunk(1800).Select(x => new string(x)).ToArray();
        await _interaction.CompleteAsync(chunks.FirstOrDefault() ?? "No settings configured.");
        foreach (var chunk in chunks.Skip(1)) await _interaction.FollowupAsync(chunk, _ephemeral);
    }
}
