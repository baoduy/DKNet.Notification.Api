namespace DKNet.Notification.AppServices.Delivery;

/// <summary>A notification in the delivery queue, with the trace id of the call that queued it.</summary>
/// <param name="Notification">A queued notification, or one back from a wait.</param>
/// <param name="TraceId">The accepting call's trace id: every delivery entry carries it, and the delivery activity links to it.</param>
public sealed record QueuedNotification(Domains.Notifications.Notification Notification, string TraceId);
