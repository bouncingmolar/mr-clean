using Discord.WebSocket;
using MrClean.Services.Filters;

namespace MrClean.Services.Retention;

public class MessageRetentionBackgroundService : BackgroundService
{
    private const int MaxDeletesPerRulePerRun = 25;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);

    private readonly DiscordSocketClient _client;
    private readonly IMessageRetentionRulesService _rules;
    private readonly MessageRetentionScanner _scanner;
    private readonly ILogger<MessageRetentionBackgroundService> _logger;

    public MessageRetentionBackgroundService(
        DiscordSocketClient client,
        IMessageRetentionRulesService rules,
        MessageRetentionScanner scanner,
        ILogger<MessageRetentionBackgroundService> logger)
    {
        _client = client;
        _rules = rules;
        _scanner = scanner;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        if (_client.ConnectionState != Discord.ConnectionState.Connected)
        {
            _logger.LogWarning(
                "Automatic retention skipped because Discord is not connected.");
            return;
        }

        var rules = await _rules.ListAsync();

        foreach (var rule in rules.Where(r => r.Enabled))
        {
            if (stoppingToken.IsCancellationRequested)
                return;

            try
            {
                var channel = _client.GetChannel(rule.ChannelId)
                              as SocketTextChannel;

                if (channel == null)
                {
                    _logger.LogWarning(
                        "Retention rule {RuleId}: channel {ChannelId} not found.",
                        rule.Id,
                        rule.ChannelId);
                    continue;
                }

                var result = await _scanner.ScanAsync(channel, rule);

                var candidates = result.DeleteCandidates
                    .Take(MaxDeletesPerRulePerRun)
                    .ToList();

                var deleted = 0;
                var failed = 0;

                foreach (var message in candidates)
                {
                    if (stoppingToken.IsCancellationRequested)
                        return;

                    try
                    {
                        await message.DeleteAsync();
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogError(
                            ex,
                            "Retention rule {RuleId}: failed deleting message {MessageId}.",
                            rule.Id,
                            message.Id);
                    }
                }

                _logger.LogInformation(
                    "Retention rule {RuleId}: scanned {Scanned}, " +
                    "{Eligible} eligible, deleted {Deleted}, failed {Failed}, " +
                    "{Replies} preserved by reply, {Reactions} preserved by reaction.",
                    rule.Id,
                    result.MessagesScanned,
                    result.DeleteCandidates.Count,
                    deleted,
                    failed,
                    result.PreservedForHumanReply.Count,
                    result.PreservedForReaction.Count);
            }
            catch (Exception ex)
            {
                // One broken rule must not prevent other rules from running.
                _logger.LogError(
                    ex,
                    "Automatic retention rule {RuleId} failed.",
                    rule.Id);
            }
        }
    }
}
