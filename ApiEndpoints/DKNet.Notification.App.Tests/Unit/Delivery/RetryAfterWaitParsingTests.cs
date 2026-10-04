using System.Net;
using DKNet.Notification.AppServices.Delivery;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>DRK-2028 §3 "Wait after a 429": how the <c>Retry-After</c> header becomes the wait.</summary>
public sealed class RetryAfterWaitParsingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    private static TimeSpan? WaitFor(string? retryAfter)
    {
        using var answer = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (retryAfter is not null)
        {
            answer.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        }

        return RetryAfterWait.From(answer.Headers, Now);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("2", 2)]
    [InlineData("59", 59)]
    [InlineData("60", 60)]
    [InlineData("61", 60)]
    [InlineData("120", 60)]
    [InlineData("Sat, 03 Oct 2026 09:00:02 GMT", 2)]
    [InlineData("Sat, 03 Oct 2026 09:00:00 GMT", 0)]
    [InlineData("Sat, 03 Oct 2026 08:59:30 GMT", 0)]
    [InlineData("Sat, 03 Oct 2026 09:05:00 GMT", 60)]
    public void A_number_of_seconds_or_a_date_is_the_wait_at_most_60_seconds(string retryAfter, int seconds)
    {
        WaitFor(retryAfter).ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("soon")]
    [InlineData("-5")]
    [InlineData("2.5")]
    public void A_missing_or_unreadable_header_gives_no_wait(string? retryAfter)
    {
        WaitFor(retryAfter).ShouldBeNull();
    }
}
