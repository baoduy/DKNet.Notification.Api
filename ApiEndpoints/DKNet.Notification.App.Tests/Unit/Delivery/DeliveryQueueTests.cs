using System.Diagnostics.Metrics;
using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>DRK-2020 §3 Step 9: the replica's queue holds at most its capacity, and its length is a gauge.</summary>
public sealed class DeliveryQueueTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly MeterListener _listener = new();
    private readonly List<int> _gauge = [];

    public DeliveryQueueTests()
    {
        var meterFactory = _services.GetRequiredService<IMeterFactory>();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, meterFactory) && instrument.Name == "notifications.queue.length")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<int>((_, value, _, _) => _gauge.Add(value));
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _services.Dispose();
    }

    private DeliveryQueue Queue(int capacity) =>
        new(new DeliverySettings(queueCapacity: capacity), new NotificationMetrics(_services.GetRequiredService<IMeterFactory>()));

    private static Domains.Notifications.Notification Received() =>
        Domains.Notifications.Notification.Receive(
            "account-opened",
            "email",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["to"] = "jane@example.com" },
            "treasury-ops");

    private static readonly RenderedMessage Message = new("Your account is open", "Dear Jane", BodyFormat.Html);

    private static EmailRecipient Jane()
    {
        EmailRecipient.TryCreate("jane@example.com", out var recipient).ShouldBeTrue();
        return recipient;
    }

    [Fact]
    public void A_queue_with_room_queues_the_notification_and_counts_it()
    {
        var queue = Queue(capacity: 2);
        var notification = Received();
        queue.Length.ShouldBe(0);

        queue.TryEnqueue(notification, Jane(), Message).ShouldBeTrue();

        queue.Length.ShouldBe(1);
        notification.Status.ShouldBe(NotificationStatus.Queued);
        notification.RenderedMessage.ShouldBeSameAs(Message);
        _listener.RecordObservableInstruments();
        _gauge.ShouldBe([1]);
    }

    [Fact]
    public void A_full_queue_refuses_and_leaves_the_notification_received()
    {
        var queue = Queue(capacity: 2);
        queue.TryEnqueue(Received(), Jane(), Message).ShouldBeTrue();
        queue.TryEnqueue(Received(), Jane(), Message).ShouldBeTrue();
        queue.Length.ShouldBe(2);
        var third = Received();

        queue.TryEnqueue(third, Jane(), Message).ShouldBeFalse();

        queue.Length.ShouldBe(2);
        third.Status.ShouldBe(NotificationStatus.Received);
        third.Recipient.ShouldBeNull();
    }

    [Fact]
    public void Calls_at_the_same_time_never_pass_the_capacity()
    {
        var queue = Queue(capacity: 50);

        var queued = Enumerable.Range(0, 200).AsParallel().Count(_ => queue.TryEnqueue(Received(), Jane(), Message));

        queued.ShouldBe(50);
        queue.Length.ShouldBe(50);
    }

    [Fact]
    public void A_missing_argument_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => new DeliveryQueue(null!, new NotificationMetrics(_services.GetRequiredService<IMeterFactory>())))
            .ParamName.ShouldBe("settings");
        Should.Throw<ArgumentNullException>(() => new DeliveryQueue(new DeliverySettings(), null!)).ParamName.ShouldBe("metrics");
        Should.Throw<ArgumentNullException>(() => Queue(1).TryEnqueue(null!, Jane(), Message)).ParamName.ShouldBe("notification");
    }
}
