namespace DKNet.Notification.Domains.Features.ManualSample.Entities;

public sealed record PurchaseOrderCreatedEvent(Guid Id, string CustomerName, decimal Amount);
