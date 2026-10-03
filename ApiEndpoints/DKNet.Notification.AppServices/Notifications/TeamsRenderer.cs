using DKNet.Notification.Domains.Notifications;
using DKNet.Notification.Domains.Templates;

namespace DKNet.Notification.AppServices.Notifications;

/// <summary>
///     Step 8 of a Teams call: fills the Teams version's title, then its Markdown body, from the parameters. Only
///     <c>{{name}}</c> is a token, matched without case, as for email. Values go in unchanged: no encoding, no line
///     break change, so a value can add Markdown but never a card element (the card's JSON writer escapes it).
/// </summary>
internal static class TeamsRenderer
{
    #region Fields

    /// <summary>The longest title a card shows; a longer filled title is cut, with no error.</summary>
    private const int MaxTitleLength = 500;

    #endregion

    #region Methods

    /// <summary>Fills <paramref name="version" /> from <paramref name="parameters" />.</summary>
    /// <param name="version">The template's Teams version.</param>
    /// <param name="parameters">The call's parameters, in the order the request body holds them.</param>
    /// <returns>
    ///     The rendered message, its title in <see cref="RenderedMessage.Subject" /> (empty for no title block), or the
    ///     first token name that has no parameter.
    /// </returns>
    public static EmailRendering Render(TemplateVersion version, IReadOnlyDictionary<string, string> parameters)
    {
        var values = parameters.ToDictionary(StringComparer.Ordinal);
        return EmailRenderer.Fill(() =>
        {
            var title = EmailRenderer.Transformer.Transform(version.Title ?? string.Empty, values);
            var body = EmailRenderer.Transformer.Transform(version.Body, values);
            return new RenderedMessage(title[..Math.Min(title.Length, MaxTitleLength)], body, BodyFormat.Markdown);
        });
    }

    #endregion
}
