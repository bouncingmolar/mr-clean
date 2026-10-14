using Microsoft.EntityFrameworkCore;
using MrClean.Data;
using MrClean.Models;

namespace MrClean.Services.Retention;

public class MessageRetentionRulesService : IMessageRetentionRulesService
{
    private readonly IDbContextFactory<MrCleanDbContext> _factory;

    public MessageRetentionRulesService(IDbContextFactory<MrCleanDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<MessageRetentionRule> GetAsync(int id)
    {
        await using var context = await _factory.CreateDbContextAsync();

        return await context.MessageRetentionRules.FirstOrDefaultAsync(r => r.Id == id)
               ?? throw new KeyNotFoundException($"Retention rule #{id} was not found.");
    }

    public async Task<List<MessageRetentionRule>> ListAsync()
    {
        await using var context = await _factory.CreateDbContextAsync();

        return await context.MessageRetentionRules
            .OrderBy(r => r.Id)
            .ToListAsync();
    }

    public async Task<MessageRetentionRule> CreateAsync(
        ulong channelId,
        ulong authorId,
        string? webhookUsername,
        int maxAgeDays,
        bool preserveHumanReplies,
        int? filterId)
    {
        ValidateMaxAge(maxAgeDays);
        ValidateTarget(authorId, webhookUsername);

        await using var context = await _factory.CreateDbContextAsync();

        var rule = new MessageRetentionRule
        {
            ChannelId = channelId,
            AuthorId = authorId,
            WebhookUsername = NormalizeWebhookUsername(webhookUsername),
            MaxAgeDays = maxAgeDays,
            PreserveHumanReplies = preserveHumanReplies,
            FilterId = filterId,
            Enabled = false
        };

        await context.MessageRetentionRules.AddAsync(rule);
        await context.SaveChangesAsync();

        return rule;
    }

    public async Task<MessageRetentionRule> EditAsync(
        int id,
        ulong channelId,
        ulong authorId,
        string? webhookUsername,
        int maxAgeDays,
        bool preserveHumanReplies,
        int? filterId)
    {
        ValidateMaxAge(maxAgeDays);
        ValidateTarget(authorId, webhookUsername);

        return await ApplyChangesAsync(id, rule =>
        {
            rule.ChannelId = channelId;
            rule.AuthorId = authorId;
            rule.WebhookUsername = NormalizeWebhookUsername(webhookUsername);
            rule.MaxAgeDays = maxAgeDays;
            rule.PreserveHumanReplies = preserveHumanReplies;
            rule.FilterId = filterId;

            return rule;
        });
    }

    public Task<MessageRetentionRule> EnableAsync(int id)
    {
        return ApplyChangesAsync(id, rule =>
        {
            rule.Enabled = true;
            return rule;
        });
    }

    public Task<MessageRetentionRule> DisableAsync(int id)
    {
        return ApplyChangesAsync(id, rule =>
        {
            rule.Enabled = false;
            return rule;
        });
    }

    public async Task<MessageRetentionRule> DeleteAsync(int id)
    {
        await using var context = await _factory.CreateDbContextAsync();

        var rule = await context.MessageRetentionRules.FirstOrDefaultAsync(r => r.Id == id)
                   ?? throw new KeyNotFoundException($"Retention rule #{id} was not found.");

        context.MessageRetentionRules.Remove(rule);
        await context.SaveChangesAsync();

        return rule;
    }

    private async Task<MessageRetentionRule> ApplyChangesAsync(
        int id,
        Func<MessageRetentionRule, MessageRetentionRule> transform)
    {
        await using var context = await _factory.CreateDbContextAsync();

        var rule = await context.MessageRetentionRules.FirstOrDefaultAsync(r => r.Id == id)
                   ?? throw new KeyNotFoundException($"Retention rule #{id} was not found.");

        var updated = transform(rule);

        await context.SaveChangesAsync();

        return updated;
    }

    private static void ValidateTarget(
        ulong authorId,
        string? webhookUsername)
    {
        if (authorId == 0 && string.IsNullOrWhiteSpace(webhookUsername))
            throw new ArgumentException(
                "Specify an author ID or webhook username.");

        if (authorId != 0 && !string.IsNullOrWhiteSpace(webhookUsername))
            throw new ArgumentException(
                "Specify an author ID or webhook username, not both.");
    }

    private static string? NormalizeWebhookUsername(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static void ValidateMaxAge(int maxAgeDays)
    {
        if (maxAgeDays < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAgeDays),
                "Maximum age must be at least one day.");
        }
    }
}
