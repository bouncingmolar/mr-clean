using System.Text.Json;
using Discord;
using MrClean.Models;

namespace MrClean.Commands.Administration;

internal static class AdminSettingsText
{
    internal static string Status(bool value) => value ? "enabled" : "disabled";
    internal static string Quote(string value) => $"`{JsonSerializer.Serialize(value).Replace("`", "\\u0060")}`";
    internal static string Management(bool enabled, bool capture) =>
        $"Global presence-role management: {Status(enabled)}; presence capture: {Status(capture)}." +
        (enabled && !capture ? " Presence-role assignments are paused while capture is disabled." : "");
    internal static string Activity(ActivityRule rule) =>
        $"Rule {rule.Id}: {rule.Type} {Quote(rule.Name)} → <@&{rule.RoleId}>; {Status(rule.Enabled)}; application {rule.ApplicationId?.ToString() ?? "any"}.";
    internal static string Fallback(ActivityType type, ulong role, bool enabled) =>
        $"{type} fallback → <@&{role}>; {Status(enabled)}.";
    internal static string Adoption(ulong role, int count) =>
        count == 0 ? $"No eligible existing grants adopted for <@&{role}>; no ownership was acquired." :
        $"Adopted {count} existing grant(s) for <@&{role}>. Ownership recorded; no member roles were directly changed by adoption. Reconciliation follows current triggers.";
    internal static string Voice(VoiceChannelRule rule) =>
        $"<#{rule.ChannelId}>: {Status(rule.Enabled)}; activity {Quote(rule.ActivityName)} ({(rule.DynamicName ? "dynamic current channel name" : "fixed")}); " +
        $"wording {Quote(rule.Wording)}; join log {Status(rule.LogJoin)}; departure log {Status(rule.LogDeparture)} → <#{rule.DestinationId}>; " +
        $"duration {Status(rule.TrackDuration)}; interruptions {Status(rule.TrackInterruptions)}; roles " +
        (rule.RoleIds.Length == 0 ? "none." : string.Join(", ", rule.RoleIds.Select(x => $"<@&{x}>")) + ".");
}
