namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     The <c>Notifications:Delivery</c> settings, read once at start-up. A value that breaks its rule stops the
///     start-up.
/// </summary>
/// <remarks>
///     Bound through the constructor, not through setters: the settings binder adds the configured items of a list to
///     the default it finds in the property, so <c>RetryDelaysSeconds</c> would keep <c>[5, 30]</c> in front of them.
/// </remarks>
/// <param name="queueCapacity">How many notifications may wait in the delivery list, for the whole service.</param>
/// <param name="maxAttempts">The attempts a notification gets at most.</param>
/// <param name="retryDelaysSeconds">The waits before attempt 2 and attempt 3; <see langword="null" /> keeps <c>[5, 30]</c>.</param>
public sealed class DeliverySettings(
    int queueCapacity = 1000,
    int maxAttempts = 3,
    IReadOnlyList<int>? retryDelaysSeconds = null)
{
    #region Fields

    /// <summary>The settings section.</summary>
    public const string SectionName = "Notifications:Delivery";

    #endregion

    #region Properties

    /// <summary>Gets how many notifications may wait in the delivery list, for the whole service: 1 to 100,000.</summary>
    public int QueueCapacity { get; } = queueCapacity;

    /// <summary>Gets the attempts a notification gets at most: 1 to 3.</summary>
    public int MaxAttempts { get; } = maxAttempts;

    /// <summary>Gets the waits before attempt 2 and attempt 3: exactly 2 items, each 1 to 300 seconds.</summary>
    public IReadOnlyList<int> RetryDelaysSeconds { get; } = retryDelaysSeconds ?? [5, 30];

    #endregion

    #region Methods

    /// <summary>Checks every setting against its rule.</summary>
    /// <exception cref="InvalidOperationException">
    ///     A setting breaks its rule. The message names the setting and never its value.
    /// </exception>
    public void Validate()
    {
        // Each message names the full setting key and its rule in words only, so it holds no number a value could be.
        if (QueueCapacity is < 1 or > 100_000)
        {
            throw Refusal(nameof(QueueCapacity), "must be from one to one hundred thousand");
        }

        if (MaxAttempts is < 1 or > 3)
        {
            throw Refusal(nameof(MaxAttempts), "must be from one to three");
        }

        if (RetryDelaysSeconds.Count != 2 || RetryDelaysSeconds.Any(delay => delay is < 1 or > 300))
        {
            throw Refusal(nameof(RetryDelaysSeconds), "must hold exactly two waits, each from one to three hundred seconds");
        }
    }

    private static InvalidOperationException Refusal(string setting, string rule) =>
        new($"The {SectionName}:{setting} setting {rule}.");

    #endregion
}
