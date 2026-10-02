using System.Diagnostics.CodeAnalysis;

namespace DKNet.Notification.Domains.Notifications;

/// <summary>
///     The one address an email goes to: exactly 1 address in <c>local@domain</c> form, at most 254 characters,
///     never trimmed. Personal data: never logged.
/// </summary>
public sealed record EmailRecipient
{
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
    public static bool TryCreate(string value, [NotNullWhen(true)] out EmailRecipient? recipient) =>
        throw new NotImplementedException();

    #endregion
}
