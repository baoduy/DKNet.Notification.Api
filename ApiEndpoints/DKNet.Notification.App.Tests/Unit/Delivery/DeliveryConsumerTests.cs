using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.Domains.Notifications;
using DomainNotification = DKNet.Notification.Domains.Notifications.Notification;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>Spec §6 "Delivering": one attempt per queued message, then a final status or the message back in the queue.</summary>
// Not serial: the meter listener only takes the meters of this class's own IMeterFactory, and the activity listener's
// source is process-wide but each test keeps only the activity of its own message id.
public sealed class DeliveryConsumerTests : IDisposable
{
    private const string TraceId = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    private static readonly DateTimeOffset Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly TestLogCapture _logs = new();
    private readonly ServiceProvider _services;
    private readonly RecordingBus _bus = new();
    private readonly NotificationStatusStore _status;
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<(string Instrument, double Value, string? Channel)> _measurements = new();
    private readonly ConcurrentDictionary<string, string?> _units = new(StringComparer.Ordinal);

    public DeliveryConsumerTests()
    {
        _services = new ServiceCollection().AddMetrics().AddLogging(l => l.AddProvider(_logs)).BuildServiceProvider();
        _status = new NotificationStatusStore(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            new NotificationStatusSettings(),
            _services.GetRequiredService<ILogger<NotificationStatusStore>>());

        var meterFactory = _services.GetRequiredService<IMeterFactory>();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, meterFactory))
            {
                _units[instrument.Name] = instrument.Unit;
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((i, value, tags, _) => Record(i, value, tags));
        _listener.SetMeasurementEventCallback<double>((i, value, tags, _) => Record(i, value, tags));
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _services.Dispose();
    }

    private DeliveryConsumer Consumer(Func<DomainNotification, CancellationToken, Task<DeliveryFailure?>> send, DeliverySettings? settings = null) =>
        new(_bus, new ScriptedSender(send), settings ?? new DeliverySettings(), _status,
            new NotificationMetrics(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            _time, _services.GetRequiredService<ILogger<DeliveryConsumer>>());

    private static DeliverNotification Message(int attemptsMade = 0, DateTimeOffset? notBefore = null) =>
        new(DeliverNotification.CurrentSchemaVersion, Guid.CreateVersion7(), "account-opened", "email", "treasury-ops",
            "order-42", Now.AddSeconds(-1), TraceId,
            "jane@example.com", null, "Your account is open", "Dear Jane", BodyFormat.Html, attemptsMade, notBefore ?? Now);

    private Task<NotificationStatusRecord?> StatusOf(DeliverNotification m) => _status.ReadAsync(m.CallerId, m.NotificationId, CancellationToken.None);

    [Fact]
    public async Task A_delivered_message_ends_success_and_is_not_queued_again()
    {
        var message = Message();
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(null)).OnHandle(message, CancellationToken.None);

        (await StatusOf(message)).ShouldBe(new NotificationStatusRecord(message.NotificationId, "order-42", NotificationOutcome.Success));
        _bus.Published.ShouldBeEmpty();
        var delivered = Entries("NotificationDelivered").ShouldHaveSingleItem();
        delivered.Level.ShouldBe(LogLevel.Information);
        ShouldCarryTheCallFields(delivered, message);
        delivered.Value("Attempt").ShouldBe("1");
        TimeSpan.Parse(delivered.Value("Duration")!, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(TimeSpan.FromSeconds(1));
        Entries("NotificationAttemptFailed").ShouldBeEmpty();
        _measurements.Where(m => m.Instrument == "notifications.delivered").ShouldHaveSingleItem().ShouldBe(("notifications.delivered", 1d, "email"));
        _measurements.Where(m => m.Instrument == "notifications.delivery.duration").ShouldHaveSingleItem().ShouldBe(("notifications.delivery.duration", 1d, "email"));
        _units["notifications.delivery.duration"].ShouldBe("s");
        _measurements.Where(m => m.Instrument == "notifications.failed").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_permanent_failure_ends_failed_at_once()
    {
        var message = Message();
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(new DeliveryFailure(false, "550"))).OnHandle(message, CancellationToken.None);

        (await StatusOf(message))!.Status.ShouldBe(NotificationOutcome.Failed);
        _bus.Published.ShouldBeEmpty();
        var attempt = Entries("NotificationAttemptFailed").ShouldHaveSingleItem();
        attempt.Level.ShouldBe(LogLevel.Warning);
        ShouldCarryTheCallFields(attempt, message);
        attempt.Value("Attempt").ShouldBe("1");
        attempt.Value("FailureKind").ShouldBe("permanent");
        attempt.Value("ReplyCode").ShouldBe("550");
        var failed = Entries("NotificationFailed").ShouldHaveSingleItem();
        failed.Level.ShouldBe(LogLevel.Error);
        ShouldCarryTheCallFields(failed, message);
        failed.Value("AttemptCount").ShouldBe("1");
        failed.Value("ReplyCode").ShouldBe("550");
        _measurements.Where(m => m.Instrument == "notifications.failed").ShouldHaveSingleItem().ShouldBe(("notifications.failed", 1d, "email"));
        _measurements.Where(m => m.Instrument == "notifications.delivered").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 30)]
    public async Task A_transient_failure_with_attempts_left_goes_back_to_the_queue_after_the_configured_wait(int attemptsMade, int waitSeconds)
    {
        var message = Message(attemptsMade);
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(new DeliveryFailure(true, "421"))).OnHandle(message, CancellationToken.None);

        var next = _bus.Published.ShouldHaveSingleItem();
        next.ShouldBe(message with { AttemptsMade = attemptsMade + 1, NotBefore = Now.AddSeconds(waitSeconds) });
        (await StatusOf(message)).ShouldBeNull(); // no final status written; pending was written at acceptance
    }

    [Fact]
    public async Task A_wait_the_provider_asks_for_replaces_the_configured_one()
    {
        var message = Message();
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(new DeliveryFailure(true, "429", TimeSpan.FromSeconds(2)))).OnHandle(message, CancellationToken.None);

        _bus.Published.ShouldHaveSingleItem().NotBefore.ShouldBe(Now.AddSeconds(2));
    }

    [Fact]
    public async Task A_transient_failure_on_the_last_attempt_ends_failed()
    {
        var message = Message(attemptsMade: 2);
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(new DeliveryFailure(true, "421"))).OnHandle(message, CancellationToken.None);

        (await StatusOf(message))!.Status.ShouldBe(NotificationOutcome.Failed);
        _bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task MaxAttempts_ends_a_transient_failure_sooner()
    {
        var message = Message();
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(new DeliveryFailure(true, "421")), new DeliverySettings(maxAttempts: 1))
            .OnHandle(message, CancellationToken.None);

        (await StatusOf(message))!.Status.ShouldBe(NotificationOutcome.Failed);
        _bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unexpected_error_ends_failed_and_does_not_escape()
    {
        var message = Message();
        await Consumer((_, _) => throw new InvalidOperationException("jane@example.com")).OnHandle(message, CancellationToken.None);

        (await StatusOf(message))!.Status.ShouldBe(NotificationOutcome.Failed);
        _logs.Messages.ShouldAllBe(m => !m.Contains("jane@example.com", StringComparison.Ordinal));
        var attempt = Entries("NotificationAttemptFailed").ShouldHaveSingleItem();
        attempt.Value("FailureKind").ShouldBe("permanent");
        attempt.Value("ReplyCode").ShouldBe(string.Empty);
        Entries("NotificationFailed").ShouldHaveSingleItem().Value("ReplyCode").ShouldBe(string.Empty);
        _measurements.Where(m => m.Instrument == "notifications.failed").ShouldHaveSingleItem().ShouldBe(("notifications.failed", 1d, "email"));
        _logs.Entries.ShouldAllBe(e => e.Exception == null);
    }

    [Fact]
    public async Task A_host_stopping_during_an_attempt_puts_the_message_back_with_its_attempts_unchanged()
    {
        var message = Message(attemptsMade: 1);
        using var stopping = new CancellationTokenSource();
        await Consumer(async (_, ct) => { await stopping.CancelAsync(); ct.ThrowIfCancellationRequested(); return null; })
            .OnHandle(message, stopping.Token);

        _bus.Published.ShouldHaveSingleItem().ShouldBe(message);
        (await StatusOf(message)).ShouldBeNull();
    }

    [Fact]
    public async Task A_message_that_is_not_due_goes_back_unchanged_without_an_attempt()
    {
        var message = Message(attemptsMade: 1, notBefore: Now.AddSeconds(30));
        var attempts = 0;
        var handling = Consumer((_, _) => { attempts++; return Task.FromResult<DeliveryFailure?>(null); }).OnHandle(message, CancellationToken.None);

        // The consumer pauses at most NotDuePause on the fake clock; move it past the pause.
        await UntilAsync(() => _bus.Published.Count == 1);
        _time.Advance(DeliveryConsumer.NotDuePause);
        await handling;

        attempts.ShouldBe(0);
        _bus.Published.ShouldHaveSingleItem().ShouldBe(message);
    }

    [Fact]
    public async Task A_retry_that_cannot_be_queued_again_ends_failed_and_does_not_escape()
    {
        var message = Message();
        _bus.ThrowOnPublish = true;
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(new DeliveryFailure(true, "421"))).OnHandle(message, CancellationToken.None);

        (await StatusOf(message))!.Status.ShouldBe(NotificationOutcome.Failed);
        _logs.Entries.ShouldContain(e => e.EventId.Name == "NotificationFailed" && e.Value("NotificationId") == message.NotificationId.ToString());
        _logs.Entries.ShouldNotContain(e => e.EventId.Name == "NotificationRequeued");
    }

    [Fact]
    public async Task A_message_that_is_not_due_and_cannot_be_queued_again_ends_failed_and_does_not_escape()
    {
        var message = Message(notBefore: Now.AddSeconds(30));
        _bus.ThrowOnPublish = true;
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(null)).OnHandle(message, CancellationToken.None);

        (await StatusOf(message))!.Status.ShouldBe(NotificationOutcome.Failed);
        _logs.Entries.ShouldContain(e => e.EventId.Name == "NotificationFailed");
    }

    [Fact]
    public async Task A_teams_message_goes_to_the_teams_sender()
    {
        var message = Message() with { Channel = "teams", EmailAddress = null, TeamsDestination = "ops-alerts", Format = BodyFormat.Markdown };
        DomainNotification? seen = null;
        var teams = new ScriptedSender((n, _) => { seen = n; return Task.FromResult<DeliveryFailure?>(null); });
        var consumer = new DeliveryConsumer(_bus, new ScriptedSender((_, _) => throw new InvalidOperationException("email sender used")),
            new DeliverySettings(), _status, new NotificationMetrics(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            _time, _services.GetRequiredService<ILogger<DeliveryConsumer>>(), teams);

        await consumer.OnHandle(message, CancellationToken.None);

        seen!.TeamsRecipient!.Name.ShouldBe("ops-alerts");
        _measurements.Where(m => m.Instrument == "notifications.delivered").ShouldHaveSingleItem().Channel.ShouldBe("teams");
        (await StatusOf(message))!.Status.ShouldBe(NotificationOutcome.Success);
    }

    [Fact]
    public async Task A_teams_message_with_no_teams_sender_ends_failed()
    {
        var message = Message() with { Channel = "teams", EmailAddress = null, TeamsDestination = "ops-alerts", Format = BodyFormat.Markdown };
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(null)).OnHandle(message, CancellationToken.None);

        (await StatusOf(message))!.Status.ShouldBe(NotificationOutcome.Failed);
        var attempt = Entries("NotificationAttemptFailed").ShouldHaveSingleItem();
        attempt.Value("Channel").ShouldBe("teams");
        attempt.Value("FailureKind").ShouldBe("permanent");
        attempt.Value("ReplyCode").ShouldBe(string.Empty);
        _measurements.Where(m => m.Instrument == "notifications.failed").ShouldHaveSingleItem().ShouldBe(("notifications.failed", 1d, "teams"));
    }

    [Theory]
    [InlineData(TraceId, true)]
    [InlineData("0HN7REQUEST:00000001", false)]
    public async Task Each_attempt_has_an_activity_of_its_own_that_links_to_the_call_trace(string traceId, bool linked)
    {
        var message = Message() with { TraceId = traceId };
        var activities = new ConcurrentQueue<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DeliveryConsumer.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Enqueue
        };
        ActivitySource.AddActivityListener(activityListener);
        // The accepting call's activity is current when the message is handled: the attempt must not become its child.
        using var caller = new Activity("caller").Start();

        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(null)).OnHandle(message, CancellationToken.None);

        var activity = activities.Where(a => Equals(a.GetTagItem("notification.id"), message.NotificationId)).ShouldHaveSingleItem();
        activity.OperationName.ShouldBe("DeliverNotification");
        activity.Kind.ShouldBe(ActivityKind.Client);
        activity.ParentSpanId.ShouldBe(default);
        activity.TraceId.ShouldNotBe(caller.TraceId);
        activity.GetTagItem("notification.attempt").ShouldBe(1);
        if (linked)
        {
            activity.Links.ShouldHaveSingleItem().Context.ShouldBe(ActivityContext.Parse(traceId, traceState: null));
        }
        else
        {
            activity.Links.ShouldBeEmpty();
        }
    }

    private CapturedLogEntry[] Entries(string eventName) =>
        _logs.Entries.Where(e => e.EventId.Name == eventName).ToArray();

    private static void ShouldCarryTheCallFields(CapturedLogEntry entry, DeliverNotification message)
    {
        entry.Value("NotificationId").ShouldBe(message.NotificationId.ToString());
        entry.Value("TemplateId").ShouldBe("account-opened");
        entry.Value("Channel").ShouldBe("email");
        entry.Value("CallerId").ShouldBe("treasury-ops");
        entry.Value("TraceId").ShouldBe(message.TraceId);
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string? channel = null;
        foreach (var tag in tags)
        {
            if (tag.Key == "channel")
            {
                channel = tag.Value as string;
            }
        }

        _measurements.Enqueue((instrument.Name, value, channel));
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException("The condition was not met within 5 seconds.");
            await Task.Delay(10);
        }
    }

    private sealed class ScriptedSender(Func<DomainNotification, CancellationToken, Task<DeliveryFailure?>> send) : IDeliverySender
    {
        public Task<DeliveryFailure?> SendAsync(DomainNotification notification, CancellationToken stoppingToken) => send(notification, stoppingToken);
    }
}
