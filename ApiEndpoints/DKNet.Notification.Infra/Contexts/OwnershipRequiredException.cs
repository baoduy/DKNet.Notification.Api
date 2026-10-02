namespace DKNet.Notification.Infra.Contexts;

/// <summary>
/// Thrown by <see cref="CoreDbContext"/> when a write would create a row with no resolvable ownership key for
/// the current caller — the caller must be refused, not attributed to nobody.
/// </summary>
public sealed class OwnershipRequiredException()
    : InvalidOperationException("The request could not be attributed to an authenticated principal.");
