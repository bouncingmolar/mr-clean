using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using MrClean.Configuration;
using MrClean.Models;
using MrClean.Services.Activities;
using MrClean.Services.VoiceRules;

namespace MrClean.Commands.Activities;

public class ActivityCommand : ISlashCommandProvider
{
    private readonly ActivityRulesService _rules;
    private readonly ActivityRoleResolver _roles;
    private readonly PresenceSnapshotCache _presence;
    private readonly VoiceRoleReconciler _reconciler;
    private readonly ActivityOptions _options;
    private readonly ulong _guild, _connected;
    private readonly ILogger<ActivityCommand> _logger;
    public ActivityCommand(ActivityRulesService rules, ActivityRoleResolver roles, PresenceSnapshotCache presence,
        VoiceRoleReconciler reconciler, IOptions<ActivityOptions> options, IOptions<DiscordOptions> discord,
        IOptions<VoiceLoggingOptions> voice, ILogger<ActivityCommand> logger)
    { _rules = rules; _roles = roles; _presence = presence; _reconciler = reconciler; _options = options.Value;
        _guild = discord.Value.GuildId; _connected = voice.Value.ConnectedRoleId; _logger = logger; }
    public ApplicationCommandProperties Properties => Build();
    private static SlashCommandOptionBuilder TypeOption(bool required) => new SlashCommandOptionBuilder()
        .WithName("type").WithDescription("Discord activity type.").WithType(ApplicationCommandOptionType.Integer).WithRequired(required)
        .AddChoice("Playing", 0).AddChoice("Streaming", 1).AddChoice("Listening", 2).AddChoice("Watching", 3);
    public static SlashCommandProperties Build()
    {
        var root = new SlashCommandBuilder().WithName("activity").WithDescription("Manage Rich Presence role rules and inspect reported activities.")
            .WithDefaultMemberPermissions(GuildPermission.ManageGuild).WithContextTypes(InteractionContextType.Guild);
        foreach (var action in new[] { "track", "edit", "untrack", "list", "fallback", "enable", "inspect", "adopt", "confirm-empty" })
        {
            var sub = new SlashCommandOptionBuilder().WithName(action).WithDescription(action + " activity configuration.")
                .WithType(ApplicationCommandOptionType.SubCommand);
            if (action is "edit" or "untrack") sub.AddOption("id", ApplicationCommandOptionType.Integer, "Rule ID from /activity list.", isRequired: true);
            if (action is "track" or "edit")
            {
                sub.AddOption("name", ApplicationCommandOptionType.String, "Exact Discord-reported activity name (case-sensitive).", isRequired: action == "track");
                sub.AddOption(TypeOption(action == "track"));
                sub.AddOption("role", ApplicationCommandOptionType.Role, "Existing role to reuse.");
                sub.AddOption("create-role", ApplicationCommandOptionType.String, "Explicitly create or safely reuse this role name.");
                sub.AddOption("application-id", ApplicationCommandOptionType.String, "Optional Discord application ID; name AND ID must match.");
                sub.AddOption("clear-application", ApplicationCommandOptionType.Boolean, "Match by name/type without an application restriction.");
                sub.AddOption("enabled", ApplicationCommandOptionType.Boolean, "Enable this rule (default true).");
            }
            if (action == "fallback")
            {
                sub.AddOption(TypeOption(true));
                sub.AddOption("enabled", ApplicationCommandOptionType.Boolean, "Enable the generic fallback.", isRequired: true);
                sub.AddOption("role", ApplicationCommandOptionType.Role, "Reuse an existing generic role.");
                sub.AddOption("create-default", ApplicationCommandOptionType.Boolean, "Explicitly create/reuse Other Games, Listening, Watching or Streaming.");
            }
            if (action == "enable")
            {
                sub.AddOption("enabled", ApplicationCommandOptionType.Boolean, "Start managing presence roles, or pause without stripping grants.", isRequired: true);
                sub.AddOption("confirm-cutover", ApplicationCommandOptionType.Boolean, "Confirm other role managers have stopped managing these roles.");
            }
            if (action is "inspect" or "confirm-empty") sub.AddOption("member", ApplicationCommandOptionType.User, "Member in this server.", isRequired: true);
            if (action is "adopt" or "confirm-empty") sub.AddOption("confirm", ApplicationCommandOptionType.Boolean, "Explicit ownership/inactivity confirmation; read the cutover guide first.", isRequired: true);
            if (action == "adopt")
            {
                // Required options must precede optional member.
                sub.AddOption("role", ApplicationCommandOptionType.Role, "Configured role whose existing grants will become Mr-Clean owned.", isRequired: true);
                sub.AddOption("member", ApplicationCommandOptionType.User, "One member, or omit for all current role holders.");
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
            if (command.User is not SocketGuildUser admin || admin.Guild.Id != _guild) throw new UnauthorizedAccessException("Use this command in the configured server.");
            VoiceRulesService.RequireAdministrator(admin.GuildPermissions.ManageGuild, admin.GuildPermissions.Administrator);
            var sub = command.Data.Options.Single(); var values = sub.Options.ToDictionary(x => x.Name, x => x.Value);
            T? Value<T>(string key) where T : class => values.TryGetValue(key, out var value) ? value as T : null;
            bool Flag(string key, bool old = false) => values.TryGetValue(key, out var value) ? (bool)value : old;
            ActivityType Type(ActivityType old = ActivityType.Playing) => values.TryGetValue("type", out var value) ? (ActivityType)Convert.ToInt32(value) : old;
            int Id() => values.TryGetValue("id", out var value) && Convert.ToInt64(value) > 0 && Convert.ToInt64(value) <= int.MaxValue
                ? Convert.ToInt32(value) : throw new ArgumentException("Choose a valid rule ID from /activity list.");
            async Task Reply(string text)
            { foreach (var chunk in text.Chunk(1800)) await command.FollowupAsync(new string(chunk), ephemeral: true, allowedMentions: AllowedMentions.None); }
            var (rules, fallbacks, enabled) = await _rules.GetAsync(_guild);
            if (sub.Name == "list")
            {
                await Reply($"Presence capture: {_options.ObservePresences}; guild management: {enabled}. Exact matching is case-sensitive.\n" +
                    string.Join("\n", rules.Select(x => $"ID {x.Id}: {x.Type} `{x.Name}` → <@&{x.RoleId}>; enabled {x.Enabled}; application {x.ApplicationId?.ToString() ?? "any"}")) +
                    "\nFallbacks:\n" + string.Join("\n", fallbacks.Select(x => $"{x.Type}: <@&{x.RoleId}>; enabled {x.Enabled}")));
                return;
            }
            if (sub.Name is "inspect" or "confirm-empty")
            {
                if (!_options.ObservePresences) throw new ArgumentException("Enable ActivityRoles:ObservePresences with the privileged intent during an approved deployment first.");
                var member = Value<SocketGuildUser>("member");
                if (member == null || member.Guild.Id != _guild || member.IsBot) throw new ArgumentException("Select a human member in this server.");
                var snapshot = _presence.Get(_guild, member.Id);
                if (sub.Name == "confirm-empty")
                {
                    if (!Flag("confirm") || member.Activities.Any(x => ActivityMatching.Supported(x.Type)))
                        throw new ArgumentException("Confirm inactivity manually first; a supported activity is still cached or confirmation was omitted.");
                    _presence.Observe(_guild, member.Id, Array.Empty<ReportedActivity>());
                    await Reply("Empty activity observation confirmed. Voice contributions remain independent."); return;
                }
                await Reply($"<@{member.Id}> presence {(snapshot.Known ? "observed" : "unknown/incomplete; not evidence of inactivity")}.\n" +
                    string.Join("\n", snapshot.Activities.Select(x => $"{x.Type}: `{System.Text.Json.JsonSerializer.Serialize(x.Name).Replace("`", "\\u0060")}`; application {x.ApplicationId?.ToString() ?? "not reported"}")));
                return;
            }
            if (sub.Name == "enable")
            {
                if (Flag("enabled") && (!_options.ObservePresences || !Flag("confirm-cutover")))
                    throw new ArgumentException("Presence capture must be enabled and confirm-cutover:true must acknowledge that competing managers stopped. Do not disable TuxedoMan without operator approval.");
                await _rules.EnableAsync(_guild, Flag("enabled"));
                await Reply("Presence management " + (Flag("enabled") ? "enabled. Inspect and explicitly adopt old grants after cutover." : "paused; existing presence contributions remain protected without re-adding absent grants.")); return;
            }
            if (sub.Name == "untrack") { await _rules.RemoveAsync(_guild, Id()); await Reply("Rule removed. Discord roles are retained; owned grants reconcile against remaining triggers."); return; }
            if (sub.Name == "adopt")
            {
                if (!Flag("confirm")) throw new ArgumentException("Explicit confirm:true is required to adopt existing role grants. Only do this after approved cutover.");
                var role = Value<SocketRole>("role") ?? throw new ArgumentException("Choose a configured role.");
                await _roles.ResolveAsync(_guild, role.Id, null, _connected);
                var selected = Value<SocketGuildUser>("member");
                if (values.ContainsKey("member") && (selected == null || selected.IsBot)) throw new ArgumentException("Select a human member in this server; invalid selections never mean bulk adoption.");
                if (selected != null && selected.Guild.Id != _guild) throw new ArgumentException("Wrong guild.");
                if (selected == null)
                {
                    await admin.Guild.DownloadUsersAsync();
                    if (admin.Guild.DownloadedMemberCount != admin.Guild.MemberCount) throw new ArgumentException("Member cache is incomplete; retry or select one member.");
                }
                var users = selected == null ? admin.Guild.Users.Where(x => !x.IsBot && x.Roles.Any(r => r.Id == role.Id)).Select(x => x.Id).ToArray() : new[] { selected.Id };
                var count = await _reconciler.AdoptAsync(_guild, users, role.Id, CancellationToken.None);
                await Reply($"Adopted {count} existing grants. No Discord roles were created, deleted or directly changed by adoption; current triggers reconcile next."); return;
            }
            if (sub.Name == "fallback")
            {
                var type = Type(); var role = Value<SocketRole>("role");
                var create = Flag("create-default") ? DefaultName(type) : null;
                var previous = fallbacks.SingleOrDefault(x => x.Type == type);
                var resolved = role != null || create != null ? await _roles.ResolveAsync(_guild, role?.Id, create, _connected) :
                    previous?.RoleId ?? throw new ArgumentException("Choose a role or explicitly request create-default:true.");
                await _rules.FallbackAsync(new ActivityFallback { GuildId = _guild, Type = type, RoleId = resolved, Enabled = Flag("enabled") }, _connected);
                await Reply("Fallback saved; it applies only to untracked activities of this type when management is enabled."); return;
            }
            if (sub.Name is not ("track" or "edit")) throw new ArgumentException("Unknown activity command.");
            var rule = sub.Name == "edit" ? rules.SingleOrDefault(x => x.Id == Id()) ?? throw new ArgumentException("Rule not found.") : new ActivityRule { GuildId = _guild };
            rule.Name = Value<string>("name") ?? rule.Name; rule.Type = Type(rule.Type); rule.Enabled = Flag("enabled", rule.Enabled);
            if (Flag("clear-application")) rule.ApplicationId = null;
            if (Value<string>("application-id") is { } raw)
                rule.ApplicationId = ulong.TryParse(raw, out var application) && application != 0 ? application : throw new ArgumentException("Application ID must be a Discord snowflake.");
            ActivityRulesService.ValidateMatch(rule.Name, rule.Type, rule.ApplicationId);
            var chosen = Value<SocketRole>("role"); var createName = Value<string>("create-role");
            if (chosen != null || createName != null || sub.Name == "track") rule.RoleId = await _roles.ResolveAsync(_guild, chosen?.Id, createName, _connected);
            await _rules.SaveAsync(rule, sub.Name == "track", _connected);
            await Reply("Activity rule saved. No dedicated role is created unless explicitly requested, and untracking never deletes roles.");
        }
        catch (Exception ex)
        {
            // No presence names/details in persistent logs.
            _logger.LogWarning("Activity administration failed ({ExceptionType}).", ex.GetType().Name);
            await command.FollowupAsync(ex is ArgumentException or UnauthorizedAccessException ? ex.Message :
                "Operation failed; inspect current configuration before retrying. No roles were automatically deleted.", ephemeral: true, allowedMentions: AllowedMentions.None);
        }
    }
    public static string DefaultName(ActivityType type) => type switch
    {
        ActivityType.Playing => "Other Games", ActivityType.Listening => "Listening", ActivityType.Watching => "Watching", ActivityType.Streaming => "Streaming",
        _ => throw new ArgumentException("Unsupported fallback type.")
    };
}
