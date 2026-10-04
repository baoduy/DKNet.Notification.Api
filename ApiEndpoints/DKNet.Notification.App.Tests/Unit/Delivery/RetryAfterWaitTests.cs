using System.Net;
using DKNet.Notification.AppServices.Delivery;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>DRK-2028 §5 rule "After a 429 the next attempt waits the time Graph asks, at most 60 seconds" (@unit).</summary>
public sealed class RetryAfterWaitTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "A wait above 60 seconds is cut to 60 seconds")]
    public void A_wait_above_60_seconds_is_cut_to_60_seconds()
    {
        // Given a Graph attempt ended with 429 and a retry after of 120 seconds
        using var answer = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        answer.Headers.Add("Retry-After", "120");

        // When the wait before the next attempt is set
        var wait = RetryAfterWait.From(answer.Headers, Now);

        // Then the wait is 60 seconds
        wait.ShouldBe(TimeSpan.FromSeconds(60));
    }
}
