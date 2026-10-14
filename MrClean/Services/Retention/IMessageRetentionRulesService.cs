using MrClean.Models;

namespace MrClean.Services.Retention;

public interface IMessageRetentionRulesService
{
    Task<MessageRetentionRule> GetAsync(int id);

    Task<List<MessageRetentionRule>> ListAsync();

    Task<MessageRetentionRule> CreateAsync(
        ulong channelId,
        ulong authorId,
        string? webhookUsername,
        int maxAgeDays,
        bool preserveHumanReplies,
        int? filterId);

    Task<MessageRetentionRule> EditAsync(
        int id,
        ulong channelId,
        ulong authorId,
        string? webhookUsername,
        int maxAgeDays,
        bool preserveHumanReplies,
        int? filterId);

    Task<MessageRetentionRule> EnableAsync(int id);

    Task<MessageRetentionRule> DisableAsync(int id);

    Task<MessageRetentionRule> DeleteAsync(int id);
}
