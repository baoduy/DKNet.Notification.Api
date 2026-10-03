using System.Net;
using DKNet.Notification.Domains.Notifications;
using DKNet.Notification.Domains.Templates;
using DKNet.Svc.Transformation;
using DKNet.Svc.Transformation.Exceptions;
using Microsoft.Extensions.Options;

namespace DKNet.Notification.AppServices.Notifications;

/// <summary>
///     Step 8 of an email call: fills the email version's subject, then its body, from the parameters. Only
///     <c>{{name}}</c> is a token, matched without case; a value is never read as a token.
/// </summary>
internal static class EmailRenderer
{
    #region Fields

    /// <summary>The longest subject a mail header line may hold (RFC 5322).</summary>
    private const int MaxSubjectLength = 998;

    // One pass over the template, so a filled value is never read again; its token cache lives for one call only.
    // Shared with the Teams renderer, so both channels read tokens the same way.
    internal static readonly TransformerService Transformer = new(Options.Create(DoubleCurlyBracketsOnly()));

    #endregion

    #region Methods

    /// <summary>Fills <paramref name="version" /> from <paramref name="parameters" />.</summary>
    /// <param name="version">The template's email version.</param>
    /// <param name="parameters">The call's parameters, in the order the request body holds them.</param>
    /// <returns>The rendered message, or the first token name that has no parameter.</returns>
    public static EmailRendering Render(TemplateVersion version, IReadOnlyDictionary<string, string> parameters)
    {
        // The resolver takes the first key equal to a token without case, so both copies keep the body's order.
        var raw = parameters.ToDictionary(StringComparer.Ordinal);
        var encoded = parameters.ToDictionary(p => p.Key, p => WebUtility.HtmlEncode(p.Value), StringComparer.Ordinal);
        return Fill(() =>
        {
            var subject = Transformer.Transform(version.Subject ?? string.Empty, raw)
                .Replace('\r', ' ')
                .Replace('\n', ' ');
            var body = Transformer.Transform(version.Body, encoded);
            return new RenderedMessage(subject[..Math.Min(subject.Length, MaxSubjectLength)], body, BodyFormat.Html);
        });
    }

    /// <summary>Runs <paramref name="render" />, turning its first token with no parameter into the rendering's answer.</summary>
    /// <param name="render">Fills every text of one version, in order, through <see cref="Transformer" />.</param>
    /// <returns>The rendered message, or the first token name that has no parameter.</returns>
    internal static EmailRendering Fill(Func<RenderedMessage> render)
    {
        try
        {
            return new EmailRendering(render(), MissingParameter: null);
        }
        catch (UnResolvedTokenException missing)
        {
            // The message is the token as the template writes it: "{{name}}".
            return new EmailRendering(Message: null, missing.Message[2..^2]);
        }
    }

    private static TransformOptions DoubleCurlyBracketsOnly()
    {
        var options = new TransformOptions();
        options.DefaultDefinitions.Clear();
        options.DefaultDefinitions.Add(TransformOptions.DoubleCurlyBrackets);
        return options;
    }

    #endregion
}

/// <summary>
///     The outcome of <see cref="EmailRenderer.Render" /> or <see cref="TeamsRenderer.Render" />: exactly one of its 2
///     members is set.
/// </summary>
/// <param name="Message">The rendered message; <see langword="null" /> when a token has no parameter.</param>
/// <param name="MissingParameter">
///     The first token name with no parameter, as the template writes it; <see langword="null" /> when every token
///     was filled.
/// </param>
internal sealed record EmailRendering(RenderedMessage? Message, string? MissingParameter);
