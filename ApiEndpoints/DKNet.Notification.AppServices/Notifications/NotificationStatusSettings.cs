namespace DKNet.Notification.AppServices.Notifications;

/// <summary>How long a status record is kept (spec §7).</summary>
/// <param name="retentionHours">Hours from the last write: 1 to 168.</param>
public sealed class NotificationStatusSettings(int retentionHours = 24)
{
    #region Fields

    /// <summary>The configuration section of these settings.</summary>
    public const string SectionName = "Notifications:Status";

    #endregion

    #region Properties

    /// <summary>Gets the hours a status record is kept after its last write.</summary>
    public int RetentionHours { get; } = retentionHours;

    #endregion

    #region Methods

    /// <summary>Refuses a value outside its rule, naming the setting and never its value.</summary>
    public void Validate()
    {
        if (RetentionHours is < 1 or > 168)
        {
            throw new InvalidOperationException(
                $"The {SectionName}:{nameof(RetentionHours)} setting must be from one to one hundred sixty-eight hours.");
        }
    }

    #endregion
}
