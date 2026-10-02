using DKNet.Notification.Domains.Services;

namespace DKNet.Notification.App.TestSupport;

public sealed class TestMembershipService : IMembershipService
{
    private int _current;

    public ValueTask<string> NextValueAsync()
    {
        var next = Interlocked.Increment(ref _current);
        return ValueTask.FromResult($"TEST-MEM-{next:D6}");
    }
}
