using System.Diagnostics.Metrics;
using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>DRK-2020 §3 Step 9: the replica's queue holds at most its capacity, and its length is a gauge.</summary>
public sealed class DeliveryQueueTests : IDisposable
{
    private const string TraceId = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

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
            "treasury-ops",
            DateTimeOffset.UtcNow);

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

        queue.TryEnqueue(notification, Jane(), Message, TraceId).ShouldBeTrue();

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
        queue.TryEnqueue(Received(), Jane(), Message, TraceId).ShouldBeTrue();
        queue.TryEnqueue(Received(), Jane(), Message, TraceId).ShouldBeTrue();
        queue.Length.ShouldBe(2);
        var third = Received();

        queue.TryEnqueue(third, Jane(), Message, TraceId).ShouldBeFalse();

        queue.Length.ShouldBe(2);
        third.Status.ShouldBe(NotificationStatus.Received);
        third.Recipient.ShouldBeNull();
    }

    [Fact]
    public void Calls_at_the_same_time_never_pass_the_capacity()
    {
        var queue = Queue(capacity: 50);

        var queued = Enumerable.Range(0, 200).AsParallel().Count(_ => queue.TryEnqueue(Received(), Jane(), Message, TraceId));

        queued.ShouldBe(50);
        queue.Length.ShouldBe(50);
    }

    private static TeamsRecipient OpsAlerts()
    {
        TeamsRecipient.TryCreate("ops-alerts", out var recipient).ShouldBeTrue();
        return recipient;
    }

    [Fact]
    public void A_Teams_notification_is_queued_with_its_destination_and_counted()
    {
        var queue = Queue(capacity: 2);
        var notification = Received();
        var card = new RenderedMessage("Account 0012345678 opened", "**Jane Tan** opened account", BodyFormat.Markdown);

        queue.TryEnqueue(notification, OpsAlerts(), card, TraceId).ShouldBeTrue();

        queue.Length.ShouldBe(1);
        notification.Status.ShouldBe(NotificationStatus.Queued);
        notification.TeamsRecipient.ShouldBe(OpsAlerts());
        notification.RenderedMessage.ShouldBeSameAs(card);
    }

    [Fact]
    public async Task Email_and_Teams_take_their_turn_in_one_line_and_share_its_places()
    {
        var queue = Queue(capacity: 2);
        var email = Received();
        var teams = Received();
        queue.TryEnqueue(email, Jane(), Message, TraceId).ShouldBeTrue();
        queue.TryEnqueue(teams, OpsAlerts(), Message, "trace-teams").ShouldBeTrue();
        var third = Received();

        queue.TryEnqueue(third, OpsAlerts(), Message, TraceId).ShouldBeFalse();
        queue.TryEnqueue(third, Jane(), Message, TraceId).ShouldBeFalse();

        queue.Length.ShouldBe(2);
        third.Status.ShouldBe(NotificationStatus.Received);
        third.TeamsRecipient.ShouldBeNull();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var inLine = new List<QueuedNotification>();
        await foreach (var queued in queue.ReadAllAsync(stop.Token))
        {
            inLine.Add(queued);
            if (inLine.Count == 2)
            {
                break;
            }
        }

        inLine.ShouldBe([new QueuedNotification(email, TraceId), new QueuedNotification(teams, "trace-teams")]);
    }

    [Fact]
    public void A_missing_argument_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => new DeliveryQueue(null!, new NotificationMetrics(_services.GetRequiredService<IMeterFactory>())))
            .ParamName.ShouldBe("settings");
        Should.Throw<ArgumentNullException>(() => new DeliveryQueue(new DeliverySettings(), null!)).ParamName.ShouldBe("metrics");
        Should.Throw<ArgumentNullException>(() => Queue(1).TryEnqueue(null!, Jane(), Message, TraceId)).ParamName.ShouldBe("notification");
        Should.Throw<ArgumentNullException>(() => Queue(1).TryEnqueue(null!, OpsAlerts(), Message, TraceId)).ParamName.ShouldBe("notification");
    }
}
