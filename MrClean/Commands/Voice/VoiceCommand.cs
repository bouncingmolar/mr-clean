using MrClean.Commands.Administration;
using MrClean.Services.Activities;
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
    private readonly ActivityRulesService _activityRules;
    private readonly ActivityOptions _activityOptions;
    private readonly ulong _guild;
    private readonly ILogger<VoiceCommand> _logger;
    public VoiceCommand(VoiceRulesService rules, VoiceRoleReconciler roles, IOptions<VoiceLoggingOptions> options, IOptions<DiscordOptions> discord, ILogger<VoiceCommand> logger, ActivityRulesService activityRules, IOptions<ActivityOptions> activityOptions)
    { _rules = rules; _roles = roles; _options = options.Value; _guild = discord.Value.GuildId; _logger = logger; _activityRules = activityRules; _activityOptions = activityOptions.Value; }
    public ApplicationCommandProperties Properties => Build();
    public static SlashCommandProperties Build()
    {
        var root = new SlashCommandBuilder().WithName("voice").WithDescription("Manage voice channel rules with public settings and confirmations.")
            .WithDefaultMemberPermissions(GuildPermission.ManageGuild).WithContextTypes(InteractionContextType.Guild);
        foreach (var action in new[] { "add", "edit", "list", "remove" })
        {
            var sub = new SlashCommandOptionBuilder().WithName(action).WithDescription(action + " voice rules (public response).")
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
        var member = command.User as SocketGuildUser;
        var authorized = member != null && member.Guild.Id == _guild &&
            (member.GuildPermissions.ManageGuild || member.GuildPermissions.Administrator);
        var sub = command.Data.Options.Single();
        var response = await AdminCommandResponse.StartAsync(new SocketAdminCommandInteraction(command),
            "voice", sub.Name, authorized, _logger);
        if (response == null) return;
        await response.ExecuteAsync(async () =>
        {
            var values = sub.Options.ToDictionary(x => x.Name, x => x.Value);
            T? Value<T>(string key) where T : class => values.TryGetValue(key, out var value) ? value as T : null;
            bool Flag(string key, bool old) => values.TryGetValue(key, out var value) ? (bool)value : old;
            var all = await _rules.ListAsync(_guild);
            var activity = await _activityRules.GetAsync(_guild);
            var management = AdminSettingsText.Management(activity.Enabled, _activityOptions.ObservePresences);
            string confirmation;
            if (sub.Name == "list")
            {
                var text = all.Count == 0 ? "No voice rules configured." : string.Join("\n", all.Select(Describe));
                return text + "\n" + management;
            }
            var channel = Value<SocketVoiceChannel>("channel");
            if (sub.Name == "remove")
            {
                var raw = Value<string>("channel-id");
                var source = channel?.Id ?? (ulong.TryParse(raw, out var parsed) ? parsed : 0);
                if (source == 0 || (channel != null && channel.Guild.Id != member!.Guild.Id)) throw new ArgumentException("Select a source or supply its valid channel ID.");
                var removed = all.SingleOrDefault(x => x.ChannelId == source) ?? throw new ArgumentException("No rule exists for this channel.");
                await _rules.RemoveAsync(_guild, source);
                confirmation = "Removed voice rule (previous settings): " + Describe(removed);
            }
            else
            {
                if (channel == null || channel is SocketStageChannel || channel.Guild.Id != member!.Guild.Id) throw new ArgumentException("Select an existing source voice channel.");
                var rule = all.SingleOrDefault(x => x.ChannelId == channel.Id);
                if (sub.Name == "edit" && rule == null) throw new ArgumentException("No rule exists for this channel.");
                if (sub.Name == "add" && rule != null) throw new ArgumentException("Rule already exists.");
                rule ??= new VoiceChannelRule { GuildId = member!.Guild.Id, ChannelId = channel.Id, ActivityName = channel.Name };
                var destination = Value<SocketTextChannel>("destination");
                if (destination != null)
                {
                    if (destination.Guild.Id != member!.Guild.Id) throw new ArgumentException("Select a destination in this server.");
                    rule.DestinationId = destination.Id;
                }
                rule.ActivityName = Value<string>("name") ?? rule.ActivityName;
                rule.Wording = Value<string>("wording") ?? rule.Wording;
                rule.Enabled = Flag("enabled", rule.Enabled); rule.DynamicName = Flag("dynamic", rule.DynamicName);
                rule.LogJoin = Flag("join-log", rule.LogJoin); rule.LogDeparture = Flag("departure-log", rule.LogDeparture);
                rule.TrackDuration = Flag("duration", rule.TrackDuration); rule.TrackInterruptions = Flag("interruptions", rule.TrackInterruptions);
                var botMember = member!.Guild.CurrentUser;
                if (!botMember.GetPermissions(channel).ViewChannel) throw new ArgumentException("Mr-Clean cannot view that voice channel.");
                var logChannel = member!.Guild.GetTextChannel(rule.DestinationId);
                if (logChannel == null) throw new ArgumentException("Select an existing destination text channel.");
                var permissions = botMember.GetPermissions(logChannel);
                if ((rule.LogJoin || rule.LogDeparture) && (!permissions.ViewChannel || !permissions.SendMessages || !permissions.ReadMessageHistory || !permissions.EmbedLinks))
                    throw new ArgumentException("The destination requires View Channel, Send Messages, Read Message History and Embed Links.");
                var roles = Flag("clear-roles", false) ? new HashSet<ulong>() : rule.RoleIds.ToHashSet();
                var add = Value<SocketRole>("add-role"); var remove = Value<SocketRole>("remove-role");
                if (add != null)
                {
                    var bot = member!.Guild.CurrentUser;
                    if (add.Guild.Id != member!.Guild.Id || add.IsManaged || add.Id == member!.Guild.Id || add.Id == _options.ConnectedRoleId ||
                        add.Permissions.Administrator || add.Permissions.ManageRoles || add.Permissions.ManageGuild ||
                        !bot.GuildPermissions.ManageRoles || add.Position >= bot.Hierarchy)
                        throw new ArgumentException("Choose an ordinary activity role below Mr-Clean; Manage Roles is required. Connected is protected.");
                    roles.Add(add.Id);
                }
                if (remove != null) roles.Remove(remove.Id);
                rule.RoleIds = roles.ToArray();
                await _rules.SaveAsync(rule, sub.Name == "add", _options.ConnectedRoleId);
                confirmation = (sub.Name == "add" ? "Added voice rule: " : "Updated voice rule: ") + Describe(rule);
            }
            // Database changes are committed first; cleanup is retryable via durable ownership.
            var reconciliation = "Role reconciliation requested; pending work retries in the background.";
            try
            {
                if (_options.Enabled) await _roles.ReconcileAsync(_guild,
                    member!.Guild.VoiceChannels.SelectMany(x => x.ConnectedUsers).Where(x => !x.IsBot).Select(x => x.Id).ToArray(), CancellationToken.None);
                else reconciliation = "Voice automation is disabled; role reconciliation was not requested.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Committed voice rule awaits reconciliation ({ExceptionType}).", ex.GetType().Name);
                reconciliation = "Rule committed; immediate role reconciliation is unavailable and will retry in the background.";
            }
            var current = await _activityRules.GetAsync(_guild);
            return confirmation + "\n" + reconciliation + "\n" + AdminSettingsText.Management(current.Enabled, _activityOptions.ObservePresences);
        });
    }
    internal static string Describe(VoiceChannelRule rule) => AdminSettingsText.Voice(rule);
}
