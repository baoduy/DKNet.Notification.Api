using System.Diagnostics.CodeAnalysis;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     The <c>Notifications:Teams</c> settings, read once at start-up. Teams is configured when it is on, its time
///     limit is 1 to 120 seconds and it has at most 100 destinations; a bad destination counts as not set.
/// </summary>
public sealed class TeamsChannelSettings
{
    #region Fields

    /// <summary>The settings section.</summary>
    public const string SectionName = "Notifications:Teams";

    #endregion

    #region Properties

    /// <summary>Gets or sets whether Teams is on. The base settings keep it off.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the time limit of each Teams attempt: 1 to 120 seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Gets the destinations by name, matched with case: at most 100.</summary>
    public Dictionary<string, TeamsDestination> Destinations { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets whether Teams is on and every Teams setting but the destinations keeps its rule.</summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Acceptance-test stub (DRK-2039): the Build (DRK-2036 row 1) writes the rule and drops this.")]
    public bool IsConfigured => false;

    #endregion

    #region Methods

    /// <summary>The webhook URL of a destination that is set.</summary>
    /// <param name="name">The destination name, as the caller sent it.</param>
    /// <returns>
    ///     The URL when <paramref name="name" /> keeps the name rule and its URL is good; otherwise
    ///     <see langword="null" />: the destination is not set.
    /// </returns>
    public Uri? WebhookFor(string name) => throw new NotImplementedException();

    #endregion
}
