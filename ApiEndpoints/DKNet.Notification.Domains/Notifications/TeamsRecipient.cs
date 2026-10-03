using System.Diagnostics.CodeAnalysis;

namespace DKNet.Notification.Domains.Notifications;

/// <summary>
///     The Teams destination a card goes to, by name: 1 to 64 characters, only lowercase letters, digits and
///     <c>-</c>, never trimmed or lower-cased. The caller sends it in <c>teamsDestination</c>. Never logged.
/// </summary>
public sealed record TeamsRecipient
{
    #region Constructors

    private TeamsRecipient(string name) => Name = name;

    #endregion

    #region Properties

    /// <summary>Gets the destination name exactly as the caller sent it.</summary>
    public string Name { get; }

    #endregion

    #region Methods

    /// <summary>Checks <paramref name="value" /> against the destination name rule.</summary>
    /// <param name="value">The <c>teamsDestination</c> parameter as the caller sent it.</param>
    /// <param name="recipient">The recipient, when the value keeps the rule.</param>
    /// <returns><see langword="true" /> when the value keeps the rule.</returns>
    public static bool TryCreate(string value, [NotNullWhen(true)] out TeamsRecipient? recipient) =>
        throw new NotImplementedException();

    #endregion
}
