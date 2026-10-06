namespace DKNet.Notification.Api.Configs.RateLimits;

public interface IRateLimitKeyProvider
{
    #region Methods

    public string GetPartitionKey(HttpContext context);

    #endregion
}

/// <summary>
///     Provides rate limiting policies based on client IP or JWT user identity
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class RateLimitKeyProvider : IRateLimitKeyProvider
{
    #region Methods

    /// <summary>
    ///     Gets the partition key for rate limiting based on authorization header or IP address.
    ///     <c>Connection.RemoteIpAddress</c> is the real caller's address as rewritten by
    ///     <c>ForwardedHeadersMiddleware</c> when the immediate peer is a configured trusted proxy
    ///     (<see cref="DKNet.Notification.Api.Configs.ForwardedHeadersConfig" />) — this deliberately never reads
    ///     <c>X-Forwarded-For</c> itself, or an untrusted peer could claim any identity and spend someone else's
    ///     budget (R1).
    /// </summary>
    public string GetPartitionKey(HttpContext context) =>
        context.User.Identity?.Name ??
        context.Connection.RemoteIpAddress?.ToString() ?? context.Request.Host.Host;

    #endregion
}