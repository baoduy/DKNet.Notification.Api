using DKNet.Notification.Domains.Notifications;
using DKNet.Notification.Domains.Templates;

namespace DKNet.Notification.AppServices.Notifications;

/// <summary>
///     Step 8 of an email call: fills the email version's subject, then its body, from the parameters. Only
///     <c>{{name}}</c> is a token, matched without case; a value is never read as a token.
/// </summary>
internal static class EmailRenderer
{
    #region Methods

    /// <summary>Fills <paramref name="version" /> from <paramref name="parameters" />.</summary>
    /// <param name="version">The template's email version.</param>
    /// <param name="parameters">The call's parameters, in the order the request body holds them.</param>
    /// <returns>The rendered message, or the first token name that has no parameter.</returns>
    public static EmailRendering Render(TemplateVersion version, IReadOnlyDictionary<string, string> parameters) =>
        throw new NotImplementedException();

    #endregion
}

/// <summary>The outcome of <see cref="EmailRenderer.Render" />: exactly one of its 2 members is set.</summary>
/// <param name="Message">The rendered message; <see langword="null" /> when a token has no parameter.</param>
/// <param name="MissingParameter">
///     The first token name with no parameter, as the template writes it; <see langword="null" /> when every token
///     was filled.
/// </param>
internal sealed record EmailRendering(RenderedMessage? Message, string? MissingParameter);
