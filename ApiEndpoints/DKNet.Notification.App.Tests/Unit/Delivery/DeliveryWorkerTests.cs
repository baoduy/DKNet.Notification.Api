using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.Domains.Notifications;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>
/// DRK-2020 §3 Delivery, at the worker: what each attempt result does, the retry clock and <c>MaxAttempts</c> (brief
/// DRK-2022 §3 row 15), with a scripted sender in place of the SMTP provider.
/// </summary>
public sealed class DeliveryWorkerTests : IAsyncDisposable
{
    private const string TraceId = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    private static readonly RenderedMessage Message = new("Your account is open", "Dear Jane", BodyFormat.Html);
    private static readonly DeliveryFailure Transient = new(IsTransient: true, ReplyCode: string.Empty);

    private readonly TestLogCapture _logs = new();
    private readonly ServiceProvider _services;
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<(string Instrument, double Value, string? Channel)> _measurements = new();
    private readonly ConcurrentDictionary<string, string?> _units = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = new();
    private Task? _running;

    public DeliveryWorkerTests()
    {
        _services = new ServiceCollection().AddMetrics().AddLogging(logging => logging.AddProvider(_logs)).BuildServiceProvider();
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

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_running is not null)
        {
            await _running;
        }

        _stop.Dispose();
        _listener.Dispose();
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task A_delivered_notification_ends_delivered_frees_its_place_and_is_logged_and_counted()
    {
        var sender = new ScriptedSender((_, _) => Task.FromResult<DeliveryFailure?>(null));
        var queue = Start(new DeliverySettings(), sender);

        var notification = Enqueue(queue);
        await UntilAsync(() => notification.Status == NotificationStatus.Delivered);

        notification.AttemptCount.ShouldBe(1);
        queue.Length.ShouldBe(0);
        var delivered = Entries("NotificationDelivered").ShouldHaveSingleItem();
        delivered.Level.ShouldBe(LogLevel.Information);
        ShouldCarryTheCallFields(delivered, notification);
        delivered.Value("Attempt").ShouldBe("1");
        TimeSpan.Parse(delivered.Value("Duration")!, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        Entries("NotificationAttemptFailed").ShouldBeEmpty();
        _measurements.Where(m => m.Instrument == "notifications.delivered").ShouldHaveSingleItem().ShouldBe(("notifications.delivered", 1d, "email"));
        var duration = _measurements.Where(m => m.Instrument == "notifications.delivery.duration").ShouldHaveSingleItem();
        duration.Channel.ShouldBe("email");
        duration.Value.ShouldBeGreaterThanOrEqualTo(0);
        _measurements.Where(m => m.Instrument == "notifications.failed").ShouldBeEmpty();
        _units["notifications.delivery.duration"].ShouldBe("s");

        // The worker goes on with the next one.
        var next = Enqueue(queue);
        await UntilAsync(() => next.Status == NotificationStatus.Delivered);
        await UntilAsync(() => queue.Length == 0);
        queue.Length.ShouldBe(0);
    }

    [Fact]
    public async Task A_permanent_failure_ends_the_notification_failed_at_once()
    {
        var sender = new ScriptedSender((_, _) => Task.FromResult<DeliveryFailure?>(new DeliveryFailure(IsTransient: false, ReplyCode: "550")));
        var queue = Start(new DeliverySettings(), sender);

        var notification = Enqueue(queue);
        await UntilAsync(() => notification.Status == NotificationStatus.Failed);

        notification.AttemptCount.ShouldBe(1);
        queue.Length.ShouldBe(0);
        var failure = Entries("NotificationAttemptFailed").ShouldHaveSingleItem();
        failure.Level.ShouldBe(LogLevel.Warning);
        ShouldCarryTheCallFields(failure, notification);
        failure.Value("Attempt").ShouldBe("1");
        failure.Value("FailureKind").ShouldBe("permanent");
        failure.Value("ReplyCode").ShouldBe("550");
        var failed = Entries("NotificationFailed").ShouldHaveSingleItem();
        failed.Level.ShouldBe(LogLevel.Error);
        ShouldCarryTheCallFields(failed, notification);
        failed.Value("AttemptCount").ShouldBe("1");
        failed.Value("ReplyCode").ShouldBe("550");
        _measurements.Where(m => m.Instrument == "notifications.failed").ShouldHaveSingleItem().ShouldBe(("notifications.failed", 1d, "email"));
        _measurements.Where(m => m.Instrument == "notifications.delivered").ShouldBeEmpty();

        await Task.Delay(TimeSpan.FromSeconds(1.5));
        sender.Attempts.Count.ShouldBe(1);
    }

    /// <summary>
    ///     DRK-2026 row 3: an error the sender did not map ends that notification Failed, and the worker goes on. A
    ///     cancellation the stop did not ask for (the sender's own) is such an error too.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unexpected_error_in_one_attempt_fails_that_notification_and_the_worker_goes_on(bool cancellation)
    {
        Exception error = cancellation
            ? new OperationCanceledException("jane@example.com")
            : new InvalidOperationException("jane@example.com");
        var calls = 0;
        var sender = new ScriptedSender((_, _) => Interlocked.Increment(ref calls) == 1
            ? throw error
            : Task.FromResult<DeliveryFailure?>(null));
        var queue = Start(new DeliverySettings(), sender);

        var notification = Enqueue(queue);
        var next = Enqueue(queue);
        await UntilAsync(() => next.Status == NotificationStatus.Delivered);

        notification.Status.ShouldBe(NotificationStatus.Failed);
        notification.AttemptCount.ShouldBe(1);
        var failure = Entries("NotificationAttemptFailed").ShouldHaveSingleItem();
        failure.Level.ShouldBe(LogLevel.Warning);
        ShouldCarryTheCallFields(failure, notification);
        failure.Value("Attempt").ShouldBe("1");
        failure.Value("FailureKind").ShouldBe("permanent");
        failure.Value("ReplyCode").ShouldBe(string.Empty);
        var failed = Entries("NotificationFailed").ShouldHaveSingleItem();
        ShouldCarryTheCallFields(failed, notification);
        failed.Value("AttemptCount").ShouldBe("1");
        failed.Value("ReplyCode").ShouldBe(string.Empty);
        Entries("NotificationDelivered").ShouldHaveSingleItem().Value("NotificationId").ShouldBe(next.NotificationId.ToString());
        _measurements.Where(m => m.Instrument == "notifications.failed").ShouldHaveSingleItem().ShouldBe(("notifications.failed", 1d, "email"));
        _measurements.Where(m => m.Instrument == "notifications.delivered").ShouldHaveSingleItem().ShouldBe(("notifications.delivered", 1d, "email"));
        queue.Length.ShouldBe(0);
        _logs.Entries.Count.ShouldBe(3);
        _logs.Entries.ShouldAllBe(e => e.Exception == null);
        _logs.Entries.SelectMany(e => e.State.Select(p => Convert.ToString(p.Value)).Append(e.Message))
            .ShouldAllBe(text => text == null || !text.Contains("jane@example.com"));
        _running.ShouldNotBeNull().IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task The_wait_before_the_next_attempt_counts_from_the_end_of_the_failed_attempt()
    {
        // Attempt 1 takes 1.5 s, longer than the 1 s wait: counted from its start, attempt 2 would follow at once.
        var sender = new ScriptedSender(async (attempt, token) =>
        {
            if (attempt > 1)
            {
                return null;
            }

            await Task.Delay(TimeSpan.FromSeconds(1.5), token);
            return Transient;
        });
        var queue = Start(new DeliverySettings(retryDelaysSeconds: [1, 1]), sender);

        var notification = Enqueue(queue);
        await UntilAsync(() => notification.Status == NotificationStatus.Delivered);

        var attempts = sender.Attempts.ToArray();
        attempts.Length.ShouldBe(2);
        (attempts[1].Start - attempts[0].End).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(0.95));
        notification.AttemptCount.ShouldBe(2);
        var failure = Entries("NotificationAttemptFailed").ShouldHaveSingleItem();
        failure.Value("FailureKind").ShouldBe("transient");
        failure.Value("ReplyCode").ShouldBe(string.Empty);
        Entries("NotificationDelivered").ShouldHaveSingleItem().Value("Attempt").ShouldBe("2");
        queue.Length.ShouldBe(0);
    }

    /// <summary>
    ///     DRK-2028 §3 "Wait after a 429" (brief DRK-2033 §3 row 8): a wait the provider asked for replaces the
    ///     configured wait (4 s here), shorter or zero; with none, the configured wait stays.
    /// </summary>
    [Theory]
    [InlineData(2d, 1.95, 3.5)]
    [InlineData(0d, 0d, 1d)]
    [InlineData(null, 3.95, 5.5)]
    public async Task The_wait_before_the_next_attempt_is_the_provider_wait_when_the_failure_carries_one(
        double? retryAfterSeconds,
        double atLeastSeconds,
        double underSeconds)
    {
        var failure = new DeliveryFailure(
            IsTransient: true,
            ReplyCode: "429",
            RetryAfter: retryAfterSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null);
        var sender = new ScriptedSender((attempt, _) => Task.FromResult(attempt == 1 ? failure : null));
        var queue = Start(new DeliverySettings(retryDelaysSeconds: [4, 4]), sender);

        var notification = Enqueue(queue);
        await UntilAsync(() => notification.Status == NotificationStatus.Delivered, TimeSpan.FromSeconds(10));

        var attempts = sender.Attempts.ToArray();
        attempts.Length.ShouldBe(2);
        (attempts[1].Start - attempts[0].End).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(atLeastSeconds));
        (attempts[1].Start - attempts[0].End).ShouldBeLessThan(TimeSpan.FromSeconds(underSeconds));
        var attemptFailure = Entries("NotificationAttemptFailed").ShouldHaveSingleItem();
        attemptFailure.Value("FailureKind").ShouldBe("transient");
        attemptFailure.Value("ReplyCode").ShouldBe("429");
        Entries("NotificationDelivered").ShouldHaveSingleItem().Value("Attempt").ShouldBe("2");
    }

    [Fact]
    public async Task A_notification_waiting_the_provider_wait_does_not_hold_up_the_next_one()
    {
        var waiting = new DeliveryFailure(IsTransient: true, ReplyCode: "429", RetryAfter: TimeSpan.FromSeconds(3));
        var firstAttempts = 0;
        var sender = new ScriptedSender((attempt, _) =>
            Task.FromResult(attempt == 1 && Interlocked.Increment(ref firstAttempts) == 1 ? waiting : null));
        var queue = Start(new DeliverySettings(retryDelaysSeconds: [1, 1]), sender);

        var notification = Enqueue(queue);
        var next = Enqueue(queue);
        await UntilAsync(() => next.Status == NotificationStatus.Delivered);

        notification.Status.ShouldBe(NotificationStatus.RetryWaiting);
        await UntilAsync(() => notification.Status == NotificationStatus.Delivered, TimeSpan.FromSeconds(10));
        notification.AttemptCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_waiting_notification_keeps_its_place_in_the_queue_and_waits_the_wait_of_its_attempt()
    {
        // The second wait (2 s) follows attempt 2, the first (1 s) follows attempt 1.
        var sender = new ScriptedSender((attempt, _) => Task.FromResult<DeliveryFailure?>(attempt < 3 ? Transient : null));
        var queue = Start(new DeliverySettings(retryDelaysSeconds: [1, 2]), sender);

        var notification = Enqueue(queue);
        await UntilAsync(() => sender.Attempts.Count == 1);
        // The worker marks the wait once the sender has returned: wait for it, then check it.
        await UntilAsync(() => notification.Status == NotificationStatus.RetryWaiting);
        notification.Status.ShouldBe(NotificationStatus.RetryWaiting);
        queue.Length.ShouldBe(1);
        await UntilAsync(() => notification.Status == NotificationStatus.Delivered, TimeSpan.FromSeconds(10));

        var attempts = sender.Attempts.ToArray();
        (attempts[1].Start - attempts[0].End).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(0.95));
        (attempts[1].Start - attempts[0].End).ShouldBeLessThan(TimeSpan.FromSeconds(1.9));
        (attempts[2].Start - attempts[1].End).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.95));
        // The worker ends the notification in the queue just after marking it delivered: wait for it, then check it.
        await UntilAsync(() => queue.Length == 0);
        queue.Length.ShouldBe(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task A_notification_ends_failed_after_MaxAttempts_transient_failures(int maxAttempts)
    {
        var sender = new ScriptedSender((_, _) => Task.FromResult<DeliveryFailure?>(Transient));
        var queue = Start(new DeliverySettings(maxAttempts: maxAttempts, retryDelaysSeconds: [1, 1]), sender);

        var notification = Enqueue(queue);
        await UntilAsync(() => notification.Status == NotificationStatus.Failed, TimeSpan.FromSeconds(10));

        notification.AttemptCount.ShouldBe(maxAttempts);
        Entries("NotificationAttemptFailed").Select(e => e.Value("Attempt"))
            .ShouldBe(Enumerable.Range(1, maxAttempts).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var failed = Entries("NotificationFailed").ShouldHaveSingleItem();
        failed.Value("AttemptCount").ShouldBe(maxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture));
        failed.Value("ReplyCode").ShouldBe(string.Empty);
        queue.Length.ShouldBe(0);

        await Task.Delay(TimeSpan.FromSeconds(1.5));
        sender.Attempts.Count.ShouldBe(maxAttempts);
    }

    [Fact]
    public async Task A_stop_during_the_wait_loses_the_notification_with_no_entry()
    {
        var sender = new ScriptedSender((_, _) => Task.FromResult<DeliveryFailure?>(Transient));
        var queue = Start(new DeliverySettings(retryDelaysSeconds: [1, 1]), sender);
        var notification = Enqueue(queue);
        await UntilAsync(() => notification.Status == NotificationStatus.RetryWaiting);

        await _stop.CancelAsync();
        await _running.ShouldNotBeNull();
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        sender.Attempts.Count.ShouldBe(1);
        notification.Status.ShouldBe(NotificationStatus.RetryWaiting);
        Entries("NotificationFailed").ShouldBeEmpty();
        queue.Length.ShouldBe(1);
    }

    [Fact]
    public async Task A_stop_during_an_attempt_ends_the_worker_with_no_entry()
    {
        var sender = new ScriptedSender(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });
        var queue = Start(new DeliverySettings(), sender);
        var notification = Enqueue(queue);
        await UntilAsync(() => notification.Status == NotificationStatus.Delivering);

        await _stop.CancelAsync();

        await Should.NotThrowAsync(_running.ShouldNotBeNull());
        notification.Status.ShouldBe(NotificationStatus.Delivering);
        _logs.Entries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(TraceId, true)]
    [InlineData("0HN7REQUEST:00000001", false)]
    public async Task Each_attempt_has_an_activity_of_its_own_that_links_to_the_call_trace(string traceId, bool linked)
    {
        var activities = new ConcurrentQueue<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DeliveryWorker.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Enqueue
        };
        ActivitySource.AddActivityListener(activityListener);
        // An activity of the test's own is current when the worker starts and while the call is queued: the attempt
        // must not become its child.
        using var caller = new Activity("caller").Start();
        var sender = new ScriptedSender((_, _) => Task.FromResult<DeliveryFailure?>(null));
        var queue = Start(new DeliverySettings(), sender);
        var notification = Enqueue(queue, traceId);
        await UntilAsync(() => notification.Status == NotificationStatus.Delivered);
        await UntilAsync(() => !activities.IsEmpty);

        var activity = activities.ShouldHaveSingleItem();
        activity.OperationName.ShouldBe("DeliverNotification");
        activity.Kind.ShouldBe(ActivityKind.Client);
        activity.ParentSpanId.ShouldBe(default);
        activity.TraceId.ShouldNotBe(caller.TraceId);
        activity.GetTagItem("notification.id").ShouldBe(notification.NotificationId);
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

    private DeliveryQueue Start(DeliverySettings settings, ScriptedSender sender)
    {
        var metrics = new NotificationMetrics(_services.GetRequiredService<IMeterFactory>());
        var queue = new DeliveryQueue(settings, metrics);
        var worker = new DeliveryWorker(queue, sender, settings, metrics, _services.GetRequiredService<ILogger<DeliveryWorker>>());
        _running = Task.Run(() => worker.RunAsync(_stop.Token));
        return queue;
    }

    private static Domains.Notifications.Notification Enqueue(DeliveryQueue queue, string traceId = TraceId)
    {
        var notification = Domains.Notifications.Notification.Receive(
            "account-opened",
            "email",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["to"] = "jane@example.com" },
            "treasury-ops");
        EmailRecipient.TryCreate("jane@example.com", out var recipient).ShouldBeTrue();
        queue.TryEnqueue(notification, recipient, Message, traceId).ShouldBeTrue();
        return notification;
    }

    private CapturedLogEntry[] Entries(string eventName) =>
        _logs.Entries.Where(e => e.EventId.Name == eventName).ToArray();

    private static void ShouldCarryTheCallFields(CapturedLogEntry entry, Domains.Notifications.Notification notification)
    {
        entry.Value("NotificationId").ShouldBe(notification.NotificationId.ToString());
        entry.Value("TemplateId").ShouldBe("account-opened");
        entry.Value("Channel").ShouldBe("email");
        entry.Value("CallerId").ShouldBe("treasury-ops");
        entry.Value("TraceId").ShouldBe(TraceId);
    }

    private static async Task UntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var waited = Stopwatch.StartNew();
        while (!condition())
        {
            waited.Elapsed.ShouldBeLessThan(timeout ?? TimeSpan.FromSeconds(5), "the worker never got there");
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
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

    /// <summary>A sender whose answer to attempt n the test gives; it records when each attempt ran.</summary>
    private sealed class ScriptedSender(Func<int, CancellationToken, Task<DeliveryFailure?>> answer) : IDeliverySender
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private readonly ConcurrentQueue<(TimeSpan Start, TimeSpan End)> _attempts = new();

        public IReadOnlyCollection<(TimeSpan Start, TimeSpan End)> Attempts => _attempts.ToArray();

        public async Task<DeliveryFailure?> SendAsync(Domains.Notifications.Notification notification, CancellationToken stoppingToken)
        {
            var start = Clock.Elapsed;
            var result = await answer(notification.AttemptCount, stoppingToken);
            _attempts.Enqueue((start, Clock.Elapsed));
            return result;
        }
    }
}
