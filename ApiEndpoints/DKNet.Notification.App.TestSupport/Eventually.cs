namespace DKNet.Notification.App.TestSupport;

/// <summary>
/// Polls a condition until it turns true or a timeout elapses. The template's memory bus is configured with
/// <c>EnableBlockingPublish = false</c> (see <c>ServiceBusSetup.AddMemoryBus</c>), so a published domain event's
/// consumers — including the log-writing handlers a scenario or test asserts on — run on a background task rather
/// than being awaited by the request/command that raised the event. Checking the captured log right after the
/// response comes back would be racy; this makes the wait explicit instead of leaving it implicit and flaky.
/// </summary>
public static class Eventually
{
    public static async Task<bool> IsTrueAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        return condition();
    }
}
