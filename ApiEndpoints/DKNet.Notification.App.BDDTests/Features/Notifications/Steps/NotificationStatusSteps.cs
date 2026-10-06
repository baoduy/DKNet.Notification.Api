using System.Diagnostics;
using DKNet.Notification.AppServices.Delivery;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.SendScenario;

namespace DKNet.Notification.App.BDDTests.Features.Notifications.Steps;

/// <summary>
/// The status and restart steps of <c>NotificationStatus.feature</c> (spec 2026-10-05 §6–§8, §10, §11). The service,
/// call and mail steps are <see cref="EmailChannelSteps" />' and <see cref="EmailDeliverySteps" />', scoped to this
/// feature too; "that notification" is the one the last send was answered with, read as the caller who sent it.
/// </summary>
[Binding]
[Scope(Feature = FeatureTitle)]
public sealed class NotificationStatusSteps(SendScenario scenario)
{
    public const string FeatureTitle = "Notification status";

    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(200);

    private IReadOnlyDictionary<string, string?>? _settings;
    private TimeSpan _firstWait;

    #region Given

    [Given(@"^the service runs with sign-in on and email set up to send to the mail catcher, and the delivery waits of (\d+) seconds? and (\d+) seconds?$")]
    public async Task GivenTheServiceRunsWithEmailSetUpAndTheDeliveryWaits(int first, int second)
    {
        var catcher = await MailCatcher.SharedAsync();
        _settings = new Dictionary<string, string?>(catcher.EmailSettings(), StringComparer.Ordinal)
        {
            ["Notifications:Delivery:RetryDelaysSeconds:0"] = Text(first),
            ["Notifications:Delivery:RetryDelaysSeconds:1"] = Text(second)
        };
        _firstWait = TimeSpan.FromSeconds(first);
        await StartAsync(keepStore: false);
    }

    #endregion

    #region When — the restart

    [When(@"^the service is stopped during the wait, with the notification left in Redis$")]
    public async Task WhenTheServiceIsStoppedDuringTheWait()
    {
        var notificationId = ThatNotification();
        var failure = EntriesFor(EmailDeliverySteps.AttemptFailedEvent, notificationId).ShouldHaveSingleItem();
        failure.Value("Attempt").ShouldBe("1");
        EntriesFor(EmailDeliverySteps.DeliveredEvent, notificationId).ShouldBeEmpty();

        await scenario.StopHostAsync();

        // Stopped before attempt 2 was due, so the first host never tried it again.
        (DateTimeOffset.UtcNow - failure.LoggedAt).ShouldBeLessThan(_firstWait, "the host must stop during the wait");
        (await RedisServer.ListLengthAsync(DeliverNotification.QueueName))
            .ShouldBe(1, $"the notification must wait in the Redis list {DeliverNotification.QueueName}");
    }

    [When(@"^the mail catcher is started$")]
    public async Task WhenTheMailCatcherIsStarted() => await (await MailCatcher.SharedAsync()).EnsureRunningAsync();

    [When(@"^the service is started again on the same Redis$")]
    public async Task WhenTheServiceIsStartedAgainOnTheSameRedis() => await StartAsync(keepStore: true);

    #endregion

    #region Then

    [Then(@"^the next host delivered it on attempt 2$")]
    public async Task ThenTheNextHostDeliveredItOnAttempt2()
    {
        var notificationId = ThatNotification();
        CapturedLogEntry? delivered = null;
        await PollAsync(
            TimeSpan.FromSeconds(5),
            () =>
            {
                // The capture is cleared once the host has started, so an early delivery is in the start-up entries.
                delivered = scenario.StartupEntries.Concat(scenario.Factory.LogCapture.Entries)
                    .FirstOrDefault(e => e.EventId.Name == EmailDeliverySteps.DeliveredEvent && e.Value("NotificationId") == notificationId);
                return Task.FromResult(delivered is not null);
            },
            () => $"the next host logged no delivery of notification {notificationId}");
        delivered!.Value("Attempt").ShouldBe("2");
    }

    [Then(@"^the status of that notification reads ""(pending|success|failed)""$")]
    public async Task ThenTheStatusOfThatNotificationReads(string status) =>
        StatusOf(await ReadAsync(), key: null).ShouldBe(status);

    [Then(@"^within (\d+) seconds the status of that notification reads ""(pending|success|failed)""$")]
    public async Task ThenWithinSecondsTheStatusOfThatNotificationReads(int seconds, string status) =>
        await StatusWithinAsync(seconds, status, key: null);

    [Then(@"^within (\d+) seconds the status of that notification reads ""(pending|success|failed)"" with the key ""([^""]*)""$")]
    public async Task ThenWithinSecondsTheStatusOfThatNotificationReadsWithTheKey(int seconds, string status, string key) =>
        await StatusWithinAsync(seconds, status, key);

    [Then(@"^""([^""]*)"" reading the status of that notification is answered 404 with ""([^""]*)""$")]
    public async Task ThenReadingTheStatusOfThatNotificationIsAnswered404With(string caller, string code)
    {
        scenario.Callers.ShouldContainKey(caller);
        caller.ShouldNotBe(scenario.LastAnswer.Caller);
        ShouldBeRefusedWith(await scenario.ReadStatusAsync(caller, ThatNotification()), HttpStatusCode.NotFound, code);
    }

    #endregion

    private async Task StartAsync(bool keepStore)
    {
        await scenario.StartAsync(signIn: true, withRedis: true, settings: _settings.ShouldNotBeNull(), keepStore: keepStore);
        scenario.StartupEntries.Where(e => e.EventId.Name == EmailChannelSteps.SenderStartedEvent)
            .ShouldHaveSingleItem().Value("Sender").ShouldBe("Smtp");
    }

    private string ThatNotification() => NotificationIdOf(scenario.LastAnswer);

    private Task<Answer> ReadAsync() => scenario.ReadStatusAsync(scenario.LastAnswer.Caller, ThatNotification());

    private CapturedLogEntry[] EntriesFor(string eventName, string notificationId) =>
        scenario.Entries(eventName).Where(e => e.Value("NotificationId") == notificationId).ToArray();

    /// <summary>Reads the status every 200 ms until it is <paramref name="status" />; the failure names the last one seen.</summary>
    private async Task StatusWithinAsync(int seconds, string status, string? key)
    {
        var last = "nothing";
        await PollAsync(
            TimeSpan.FromSeconds(seconds),
            async () =>
            {
                var answer = await ReadAsync();
                last = answer.Status == HttpStatusCode.OK ? StatusOf(answer, key) : $"HTTP {(int)answer.Status} {answer.Body}";
                return last == status;
            },
            () => $"the status of notification {ThatNotification()} did not read \"{status}\"; the last read was \"{last}\"");
    }

    /// <summary>The status of a 200 that holds exactly the notification id, its key and the status.</summary>
    private string StatusOf(Answer answer, string? key)
    {
        answer.Status.ShouldBe(HttpStatusCode.OK, answer.Body);
        using var json = JsonDocument.Parse(answer.Body);
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["notificationId", "idempotencyKey", "status"]);
        json.RootElement.GetProperty("notificationId").GetString().ShouldBe(ThatNotification());
        if (key is not null)
        {
            json.RootElement.GetProperty("idempotencyKey").GetString().ShouldBe(key);
        }

        return json.RootElement.GetProperty("status").GetString().ShouldNotBeNull();
    }

    private static async Task PollAsync(TimeSpan timeout, Func<Task<bool>> probe, Func<string> failure)
    {
        var waited = Stopwatch.StartNew();
        while (!await probe())
        {
            if (waited.Elapsed > timeout)
            {
                throw new ShouldAssertException($"{failure()} within {timeout.TotalSeconds:0.#} s");
            }

            await Task.Delay(PollEvery);
        }
    }

    private static string Text(int number) => number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
