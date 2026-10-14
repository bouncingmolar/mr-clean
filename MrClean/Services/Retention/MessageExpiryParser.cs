using System.Globalization;
using System.Text.RegularExpressions;
using Discord;
using MrClean.Services;

namespace MrClean.Services.Retention;

public static class MessageExpiryParser
{
    private static readonly Regex DiscordTimestamp = new(
        @"\buntil\s+<t:(\d+)(?::[A-Za-z])?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WrittenDate = new(
        @"\buntil\s+(\d{1,2}/\d{1,2}/\d{4})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static DateTimeOffset? GetExpiry(IMessage message)
    {
        foreach (var text in MessageTextExtractor.GetTexts(message))
        {
            var discord = DiscordTimestamp.Match(text);
            if (discord.Success &&
                long.TryParse(discord.Groups[1].Value, out var unix))
            {
                return DateTimeOffset.FromUnixTimeSeconds(unix);
            }

            var written = WrittenDate.Match(text);
            if (written.Success &&
                DateTime.TryParseExact(
                    written.Groups[1].Value,
                    "d/M/yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                // Date-only expiry: keep it for the entire stated day.
                return new DateTimeOffset(
                    date.Year, date.Month, date.Day,
                    23, 59, 59, TimeSpan.Zero);
            }
        }

        return null;
    }

    public static bool IsExpired(IMessage message, DateTimeOffset now)
    {
        var expiry = GetExpiry(message);
        return expiry.HasValue && expiry.Value <= now;
    }
}
