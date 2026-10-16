using System.Text.RegularExpressions;
using Discord;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MrClean.Configuration;
using MrClean.Data;
using MrClean.Models;

namespace MrClean.Services;

public class MessageFilteringService
{
    private readonly DiscordOptions _options;

    private readonly IDbContextFactory<MrCleanDbContext> _factory;

    private readonly ILogger<MessageFilteringService> _logger;

    public MessageFilteringService(IOptions<DiscordOptions> options, IDbContextFactory<MrCleanDbContext> factory, ILogger<MessageFilteringService> logger)
    {
        _factory = factory;
        _logger = logger;
        _options = options.Value;
    }

    public void RegisterMessageHandler(DiscordSocketClient client)
    {
        client.MessageReceived += async message => await HandleMessageEventAsync(message, client);
    }

    private async Task HandleMessageEventAsync(SocketMessage message, BaseSocketClient client)
    {
        try
        {
            // If the message was not sent in the managed guild
            if (
                message.Author.IsWebhook ||
                message.Channel is not SocketGuildChannel channel ||
                message.Author.Id == client.CurrentUser.Id ||
                channel.Guild.Id != _options.GuildId
            )
            {
                return;
            }

            await using var context = await _factory.CreateDbContextAsync();

            var filters = await context.MessageFilters.Where(f => f.Enabled).ToListAsync();
            var member = channel.Guild.GetUser(message.Author.Id) ?? throw new Exception("Cannot find member");
            var matching = filters.FirstOrDefault(f => Matches(f, message, member));

            if (matching == null)
            {
                return;
            }

            await Task.Delay(matching.Delay * 1000);

            if (matching.RepostChannelId is not null)
            {
                var repostChannel = channel.Guild.GetTextChannel(matching.RepostChannelId.Value);

                // If there are no webhooks in this channel, create a new one
                var webhooks = await repostChannel.GetWebhooksAsync();
                var webhook = webhooks.FirstOrDefault(w => w.Name == "mr-clean-webhook")
                              ?? await repostChannel.CreateWebhookAsync("mr-clean-webhook");

                var webhookClient = new DiscordWebhookClient(webhook);
                var repostedMessageId = await webhookClient.SendMessageAsync(
                    message.Content,
                    username: message.Author.Username,
                    avatarUrl: message.Author.GetAvatarUrl(),
                    embeds: message.Embeds.ToArray()
                );

                if (message.Attachments.Count != 0)
                {
                    // Repost all message attachments (files, images, videos...)
                    var httpClient = new HttpClient();
                    var attachments = await Task.WhenAll(
                        message.Attachments.Select(async a =>
                            new FileAttachment(await httpClient.GetStreamAsync(a.Url),
                                a.Filename,
                                a.Filename,
                                a.IsSpoiler()
                            )
                        )
                    );

                    await repostChannel.GetMessageAsync(repostedMessageId);
                    await webhookClient.SendFilesAsync(
                        text: string.Empty,
                        attachments: attachments,
                        username: message.Author.Username,
                        avatarUrl: message.Author.GetAvatarUrl()
                    );
                }
            }

            await message.DeleteAsync();
        }
        catch (Exception exception)
        {
            _logger.LogCritical("Exception during handling message: {exception}", exception.Message);
            throw;
        }
    }

    private static bool Matches(MessageFilter filter, SocketMessage message, SocketGuildUser member)
    {
        var pattern = new Regex(filter.Pattern ?? ".*");

        if (!pattern.IsMatch(message.CleanContent))
        {
            // Check content of all embeds for a match against the regex
            var match = message.Embeds.Any(e => MatchesEmbed(pattern, e));

            if (!match) return false;
        }

        // There are no denied roles, return false
        if (member.Roles.Any(r => filter.Roles.DeniedEntities.Contains(r.Id))) return false;

        return filter.Channels.AllowsEntity(message.Channel.Id) &&
               filter.Users.AllowsEntity(message.Author.Id) &&
               member.Roles.Any(r => filter.Roles.AllowsEntity(r.Id));
    }
    internal static bool MatchesPattern(MessageFilter filter, IMessage message)
    {
        var pattern = new Regex(filter.Pattern ?? ".*");

        return MessageTextExtractor.GetTexts(message)
            .Any(text => pattern.IsMatch(text));
    }

    internal static bool MatchesEmbed(Regex pattern, IEmbed embed)
    {
        var texts = new[]
            {
                embed.Title,
                embed.Description,
                embed.Author?.Name,
                embed.Footer?.Text,
                embed.Url
            }
            .Concat(embed.Fields.SelectMany(f => new[] { f.Name, f.Value }))
            .Where(text => !string.IsNullOrEmpty(text));

        return texts.Any(text => pattern.IsMatch(text!));
    }


}