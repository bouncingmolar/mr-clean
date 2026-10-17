using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using MrClean.Configuration;
using MrClean.Models;
using MrClean.Services.VoiceRules;

namespace MrClean.Commands.Voice;

public class VoiceCommand : ISlashCommandProvider
{
    private readonly VoiceRulesService _rules;
    private readonly VoiceRoleReconciler _roles;
    private readonly VoiceLoggingOptions _options;
    private readonly ulong _guild;
    private readonly ILogger<VoiceCommand> _logger;
    public VoiceCommand(VoiceRulesService rules, VoiceRoleReconciler roles, IOptions<VoiceLoggingOptions> options, IOptions<DiscordOptions> discord, ILogger<VoiceCommand> logger)
    { _rules = rules; _roles = roles; _options = options.Value; _guild = discord.Value.GuildId; _logger = logger; }
    public ApplicationCommandProperties Properties => Build();
    public static SlashCommandProperties Build()
    {
        var root = new SlashCommandBuilder().WithName("voice").WithDescription("Manage voice channel rules.")
            .WithDefaultMemberPermissions(GuildPermission.ManageGuild).WithContextTypes(InteractionContextType.Guild);
        foreach (var action in new[] { "add", "edit", "list", "remove" })
        {
            var sub = new SlashCommandOptionBuilder().WithName(action).WithDescription(action + " voice rules.")
                .WithType(ApplicationCommandOptionType.SubCommand);
            if (action is "add" or "edit") sub.AddOption(new SlashCommandOptionBuilder().WithName("channel")
                .WithDescription("Source voice channel.").WithType(ApplicationCommandOptionType.Channel)
                .AddChannelType(ChannelType.Voice).WithRequired(true));
            if (action == "remove")
            {
                sub.AddOption(new SlashCommandOptionBuilder().WithName("channel").WithDescription("Source voice channel.")
                    .WithType(ApplicationCommandOptionType.Channel).AddChannelType(ChannelType.Voice));
                sub.AddOption("channel-id", ApplicationCommandOptionType.String, "Source ID, for a deleted channel.");
            }
            if (action is "add" or "edit")
            {
                sub.AddOption(new SlashCommandOptionBuilder().WithName("destination").WithDescription("Log text channel.")
                    .WithType(ApplicationCommandOptionType.Channel).AddChannelType(ChannelType.Text).WithRequired(action == "add"));
                sub.AddOption("name", ApplicationCommandOptionType.String, "Fixed activity display name.");
                sub.AddOption("wording", ApplicationCommandOptionType.String, "Activity wording; optional {activity} placeholder.");
                foreach (var option in new[] { "enabled", "dynamic", "join-log", "departure-log", "duration", "interruptions" })
                    sub.AddOption(option, ApplicationCommandOptionType.Boolean, "Set " + option + ".");
                sub.AddOption("add-role", ApplicationCommandOptionType.Role, "Add a managed activity role (repeat edit to add more).");
                sub.AddOption("remove-role", ApplicationCommandOptionType.Role, "Remove a managed activity role.");
                sub.AddOption("clear-roles", ApplicationCommandOptionType.Boolean, "Clear all activity roles.");
            }
            root.AddOption(sub);
        }
        return root.Build();
    }

    public async Task HandleCommandInvocationAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);
        try
        {
            if (command.User is not SocketGuildUser member || member.Guild.Id != _guild)
                throw new UnauthorizedAccessException("Use this command in the configured server.");
            VoiceRulesService.RequireAdministrator(member.GuildPermissions.ManageGuild, member.GuildPermissions.Administrator);
            var sub = command.Data.Options.Single();
            var values = sub.Options.ToDictionary(x => x.Name, x => x.Value);
            T? Value<T>(string key) where T : class => values.TryGetValue(key, out var value) ? value as T : null;
            bool Flag(string key, bool old) => values.TryGetValue(key, out var value) ? (bool)value : old;
            var all = await _rules.ListAsync(member.Guild.Id);
            if (sub.Name == "list")
            {
                var text = all.Count == 0 ? "No voice rules configured." : string.Join("\n", all.Select(Describe));
                foreach (var chunk in text.Chunk(1800)) await command.FollowupAsync(new string(chunk), ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }
            var channel = Value<SocketVoiceChannel>("channel");
            if (sub.Name == "remove")
            {
                var raw = Value<string>("channel-id");
                var source = channel?.Id ?? (ulong.TryParse(raw, out var parsed) ? parsed : 0);
                if (source == 0 || (channel != null && channel.Guild.Id != member.Guild.Id)) throw new ArgumentException("Select a source or supply its valid channel ID.");
                await _rules.RemoveAsync(member.Guild.Id, source);
            }
            else
            {
                if (channel == null || channel is SocketStageChannel || channel.Guild.Id != member.Guild.Id) throw new ArgumentException("Select an existing source voice channel.");
                var rule = all.SingleOrDefault(x => x.ChannelId == channel.Id);
                if (sub.Name == "edit" && rule == null) throw new ArgumentException("No rule exists for this channel.");
                if (sub.Name == "add" && rule != null) throw new ArgumentException("Rule already exists.");
                rule ??= new VoiceChannelRule { GuildId = member.Guild.Id, ChannelId = channel.Id, ActivityName = channel.Name };
                var destination = Value<SocketTextChannel>("destination");
                if (destination != null)
                {
                    if (destination.Guild.Id != member.Guild.Id) throw new ArgumentException("Select a destination in this server.");
                    rule.DestinationId = destination.Id;
                }
                rule.ActivityName = Value<string>("name") ?? rule.ActivityName;
                rule.Wording = Value<string>("wording") ?? rule.Wording;
                rule.Enabled = Flag("enabled", rule.Enabled); rule.DynamicName = Flag("dynamic", rule.DynamicName);
                rule.LogJoin = Flag("join-log", rule.LogJoin); rule.LogDeparture = Flag("departure-log", rule.LogDeparture);
                rule.TrackDuration = Flag("duration", rule.TrackDuration); rule.TrackInterruptions = Flag("interruptions", rule.TrackInterruptions);
                var botMember = member.Guild.CurrentUser;
                if (!botMember.GetPermissions(channel).ViewChannel) throw new ArgumentException("Mr-Clean cannot view that voice channel.");
                var logChannel = member.Guild.GetTextChannel(rule.DestinationId);
                if (logChannel == null) throw new ArgumentException("Select an existing destination text channel.");
                var permissions = botMember.GetPermissions(logChannel);
                if ((rule.LogJoin || rule.LogDeparture) && (!permissions.ViewChannel || !permissions.SendMessages || !permissions.ReadMessageHistory || !permissions.EmbedLinks))
                    throw new ArgumentException("The destination requires View Channel, Send Messages, Read Message History and Embed Links.");
                var roles = Flag("clear-roles", false) ? new HashSet<ulong>() : rule.RoleIds.ToHashSet();
                var add = Value<SocketRole>("add-role"); var remove = Value<SocketRole>("remove-role");
                if (add != null)
                {
                    var bot = member.Guild.CurrentUser;
                    if (add.Guild.Id != member.Guild.Id || add.IsManaged || add.Id == member.Guild.Id || add.Id == _options.ConnectedRoleId ||
                        add.Permissions.Administrator || add.Permissions.ManageRoles || add.Permissions.ManageGuild ||
                        !bot.GuildPermissions.ManageRoles || add.Position >= bot.Hierarchy)
                        throw new ArgumentException("Choose an ordinary activity role below Mr-Clean; Manage Roles is required. Connected is protected.");
                    roles.Add(add.Id);
                }
                if (remove != null) roles.Remove(remove.Id);
                rule.RoleIds = roles.ToArray();
                await _rules.SaveAsync(rule, sub.Name == "add", _options.ConnectedRoleId);
            }
            // Database changes are committed first; cleanup is retryable via durable ownership.
            if (_options.Enabled) await _roles.ReconcileAsync(member.Guild.Id,
                member.Guild.VoiceChannels.SelectMany(x => x.ConnectedUsers).Where(x => !x.IsBot).Select(x => x.Id).ToArray(), CancellationToken.None);
            await command.FollowupAsync("Voice rule saved. Activity role changes reconcile now and on the next background pass; failures retain cleanup ownership for retry.", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Voice rule command failed.");
            await command.FollowupAsync(ex is ArgumentException or UnauthorizedAccessException ? ex.Message :
                "Unable to complete the request; check bot logs. Committed rules, if any, remain available through /voice list.", ephemeral: true, allowedMentions: AllowedMentions.None);
        }
    }
    internal static string Describe(VoiceChannelRule rule) =>
        $"<#{rule.ChannelId}>: {(rule.Enabled ? "enabled" : "disabled")}; {rule.ActivityName}; {(rule.DynamicName ? "dynamic" : "fixed")}; wording `{rule.Wording}`; logs {rule.LogJoin}/{rule.LogDeparture} → <#{rule.DestinationId}>; duration {rule.TrackDuration}; interruptions {rule.TrackInterruptions}; roles {(rule.RoleIds.Length == 0 ? "none" : string.Join(", ", rule.RoleIds.Select(x => $"<@&{x}>")))}";
}
