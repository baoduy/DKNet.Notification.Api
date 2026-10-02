namespace DKNet.Notification.AppServices.Extensions;

/// <summary>
///
/// </summary>
/// <param name="entityType"></param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MapsFromAttribute(Type entityType) : Attribute
{
    #region Properties

    /// <summary>
    ///
    /// </summary>
    public Type EntityType { get; } = entityType;

    #endregion
}