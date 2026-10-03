using System.Net.Http.Headers;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>Reads the <c>Retry-After</c> header of a provider's 429 answer as the wait before the next attempt.</summary>
public static class RetryAfterWait
{
    /// <summary>
    ///     The wait the answer asks for: a number of seconds, or an HTTP date counted from <paramref name="now" />, at
    ///     most 60 seconds.
    /// </summary>
    /// <param name="headers">The headers of the provider's answer.</param>
    /// <param name="now">The time the answer came, for a date.</param>
    /// <returns>
    ///     The wait; zero for <c>0</c> or a date in the past; <see langword="null" /> when the header is missing or
    ///     cannot be read.
    /// </returns>
    public static TimeSpan? From(HttpResponseHeaders headers, DateTimeOffset now) =>
        throw new NotImplementedException();
}
