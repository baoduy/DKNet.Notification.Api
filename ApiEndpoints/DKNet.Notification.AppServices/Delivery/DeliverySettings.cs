namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     The <c>Notifications:Delivery</c> settings, read once at start-up. A value that breaks its rule stops the
///     start-up.
/// </summary>
public sealed class DeliverySettings
{
    #region Fields

    /// <summary>The settings section.</summary>
    public const string SectionName = "Notifications:Delivery";

    #endregion

    #region Properties

    /// <summary>Gets or sets how many notifications per replica may not have ended: 1 to 100,000.</summary>
    public int QueueCapacity { get; set; } = 1000;

    /// <summary>Gets or sets the attempts a notification gets at most: 1 to 3.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Gets or sets the waits before attempt 2 and attempt 3: exactly 2 items, each 1 to 300 seconds.</summary>
    public IReadOnlyList<int> RetryDelaysSeconds { get; set; } = [5, 30];

    #endregion

    #region Methods

    /// <summary>Checks every setting against its rule.</summary>
    /// <exception cref="InvalidOperationException">
    ///     A setting breaks its rule. The message names the setting and never its value.
    /// </exception>
    public void Validate() => throw new NotImplementedException();

    #endregion
}
