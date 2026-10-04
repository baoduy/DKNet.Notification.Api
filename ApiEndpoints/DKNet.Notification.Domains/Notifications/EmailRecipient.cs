using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;

namespace DKNet.Notification.Domains.Notifications;

/// <summary>
///     The one address an email goes to: exactly 1 address in <c>local@domain</c> form whose local part is an
///     unquoted dot-atom, at most 254 characters, never trimmed. Personal data: never logged.
/// </summary>
public sealed record EmailRecipient
{
    #region Fields

    /// <summary>The longest address the rule allows.</summary>
    public const int MaxLength = 254;

    #endregion

    #region Constructors

    private EmailRecipient(string address) => Address = address;

    #endregion

    #region Properties

    /// <summary>Gets the address exactly as the caller sent it.</summary>
    public string Address { get; }

    #endregion

    #region Methods

    /// <summary>Checks <paramref name="value" /> against the recipient rule.</summary>
    /// <param name="value">The <c>to</c> parameter as the caller sent it.</param>
    /// <param name="recipient">The recipient, when the value keeps the rule.</param>
    /// <returns><see langword="true" /> when the value keeps the rule.</returns>
    public static bool TryCreate(string value, [NotNullWhen(true)] out EmailRecipient? recipient)
    {
        // MailAddress also takes a display name, a comment or surrounding white space; only a parse that gives the
        // value back unchanged is 1 bare address. A quoted local part may hold a space, so white space is refused too.
        // MailAddress also takes a local part ending in a dot (jane.@example.com), which a mail header cannot hold, so
        // the local part must be an RFC 5322 dot-atom: no empty atom (leading, trailing or doubled dot), no quote.
        recipient = value.Length <= MaxLength
                    && !value.Any(char.IsWhiteSpace)
                    && MailAddress.TryCreate(value, out var address)
                    && string.Equals(address.Address, value, StringComparison.Ordinal)
                    && value[..value.LastIndexOf('@')].Split('.').All(atom => atom.Length > 0 && !atom.Contains('"', StringComparison.Ordinal))
            ? new EmailRecipient(value)
            : null;
        return recipient is not null;
    }

    #endregion
}
