using Discord;

namespace MrClean.Services;

internal static class MessageTextExtractor
{
    public static IEnumerable<string> GetTexts(IMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Content))
            yield return message.Content;

        foreach (var embed in message.Embeds)
        {
            if (!string.IsNullOrWhiteSpace(embed.Title))
                yield return embed.Title;

            if (!string.IsNullOrWhiteSpace(embed.Description))
                yield return embed.Description;

            if (embed.Author.HasValue &&
                !string.IsNullOrWhiteSpace(embed.Author.Value.Name))
                yield return embed.Author.Value.Name;

            if (embed.Footer.HasValue &&
                !string.IsNullOrWhiteSpace(embed.Footer.Value.Text))
                yield return embed.Footer.Value.Text;

            if (!string.IsNullOrWhiteSpace(embed.Url))
                yield return embed.Url;

            foreach (var field in embed.Fields)
            {
                if (!string.IsNullOrWhiteSpace(field.Name))
                    yield return field.Name;

                if (!string.IsNullOrWhiteSpace(field.Value))
                    yield return field.Value;
            }
        }

        foreach (var component in message.Components)
        {
            foreach (var text in GetComponentTexts(component))
                yield return text;
        }
    }

    private static IEnumerable<string> GetComponentTexts(
        IMessageComponent component)
    {
        if (component is TextDisplayComponent textDisplay &&
            !string.IsNullOrWhiteSpace(textDisplay.Content))
        {
            yield return textDisplay.Content;
        }

        if (component is INestedComponent nested)
        {
            foreach (var child in nested.Components)
            {
                foreach (var text in GetComponentTexts(child))
                    yield return text;
            }
        }
    }
}
