using System.Text.RegularExpressions;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.EmailDeliverySteps;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.SendScenario;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.TeamsScenario;

namespace DKNet.Notification.App.BDDTests.Features.Notifications.Steps;

/// <summary>
/// The Then steps of <c>TeamsChannel.feature</c> (DRK-2035 §5) about delivery: what the webhook stub received, the
/// card, the attempts and their waits, and what the logs, counters, traces and kept records hold. Every expected value
/// is a literal from the spec; the card's shape is the one of §3 "Rendering and the card".
/// </summary>
[Binding]
[Scope(Feature = FeatureTitle)]
public sealed class TeamsDeliverySteps(SendScenario scenario, TeamsScenario teams)
{
    // Longer than the wait before attempt 2 (5 s), so a second attempt would have started.
    private static readonly TimeSpan PastAttempt2 = TimeSpan.FromSeconds(7);

    #region Then — the post and the card

    [Then(@"^the webhook stub records 1 post: a message with 1 Adaptive Card$")]
    public async Task ThenTheWebhookStubRecords1PostAMessageWith1AdaptiveCard() =>
        TextBlocksOf(await SinglePostAsync()).ShouldNotBeEmpty();

    [Then(@"^the card holds the title ""([^""]*)"" and the Markdown text ""([^""]*)""$")]
    public async Task ThenTheCardHoldsTheTitleAndTheMarkdownText(string title, string text)
    {
        var blocks = TextBlocksOf(await SinglePostAsync());
        blocks.Select(TextOf).ShouldBe([title, text]);
    }

    [Then(@"^both text blocks wrap, and the mail catcher receives nothing$")]
    public async Task ThenBothTextBlocksWrapAndTheMailCatcherReceivesNothing()
    {
        var blocks = TextBlocksOf(await SinglePostAsync());
        blocks.Length.ShouldBe(2);
        foreach (var block in blocks)
        {
            block.GetProperty("wrap").ValueKind.ShouldBe(JsonValueKind.True);
        }

        teams.MailCatcher.IsRunning.ShouldBeTrue("the mail catcher must be ready to receive");
        (await teams.MailCatcher.MailCountAsync()).ShouldBe(0);
    }

    [Then(@"^the webhook stub records 1 post$")]
    public async Task ThenTheWebhookStubRecords1Post() =>
        (await SinglePostAsync()).Path.ShouldBe(PathOfWebhook);

    [Then(@"^the webhook stub records 1 post with exactly 1 Adaptive Card of 2 text blocks$")]
    public async Task ThenTheWebhookStubRecords1PostWithExactly1AdaptiveCardOf2TextBlocks() =>
        TextBlocksOf(await SinglePostAsync()).Length.ShouldBe(2);

    [Then(@"^the Markdown text holds the customer value exactly as sent$")]
    public async Task ThenTheMarkdownTextHoldsTheCustomerValueExactlyAsSent()
    {
        // The template body "**{{customerName}}** opened account {{accountNumber}}." filled with the values sent.
        var blocks = TextBlocksOf(await SinglePostAsync());
        TextOf(blocks[^1]).ShouldBe($"**{teams.LastCustomer}** opened account {AccountNumber}.");
    }

    [Then(@"^the card's Markdown text is ""([^""]*)""$")]
    public async Task ThenTheCardsMarkdownTextIs(string text) =>
        TextOf(TextBlocksOf(await SinglePostAsync())[^1]).ShouldBe(text);

    [Then(@"^the posted card holds (no title block|the title of all 500 characters|the first 500 characters as title) and the Markdown text ""([^""]*)""$")]
    public async Task ThenThePostedCardHoldsAndTheMarkdownText(string titleBlock, string text)
    {
        var texts = TextBlocksOf(await SinglePostAsync()).Select(TextOf).ToArray();
        string?[] expected = titleBlock switch
        {
            "no title block" => [text],
            // The headline sent: 499 "h" and a "Z".
            "the title of all 500 characters" => [new string('h', 499) + "Z", text],
            // The headline sent was 500 "h" and a "Z": the "Z" is cut.
            _ => [new string('h', 500), text]
        };
        texts.ShouldBe(expected);
    }

    [Then(@"^the call is accepted, and the webhook stub records 1 post of ([\d,]+) bytes$")]
    public async Task ThenTheCallIsAcceptedAndTheWebhookStubRecords1PostOfBytes(string size)
    {
        scenario.ShouldBeAccepted(scenario.LastAnswer);
        var bytes = int.Parse(size, System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture);
        Encoding.UTF8.GetByteCount((await SinglePostAsync()).Body).ShouldBe(bytes);
    }

    [Then(@"^the webhook stub records 1 post, for ""([^""]*)""$")]
    public async Task ThenTheWebhookStubRecords1PostFor(string destination)
    {
        await teams.DeliveryAsync(teams.NotificationOf(destination), 1, TimeSpan.FromSeconds(15));
        // The destination's own path: a post to the other destination's URL would carry another one.
        (await SinglePostAsync()).Path.ShouldBe(PathOfWebhook);
    }

    [Then(@"^the webhook stub records no post$")]
    public async Task ThenTheWebhookStubRecordsNoPost()
    {
        await teams.AttemptFailureAsync(NotificationIdOf(scenario.LastAnswer), 1, TimeSpan.FromSeconds(30));
        teams.WebhookStub.IsRunning.ShouldBeTrue("the webhook stub must be ready to receive");
        teams.WebhookStub.Requests.ShouldBeEmpty();
    }

    #endregion

    #region Then — logs, counts and traces

    [Then(@"^1 queued entry and 1 delivered entry on attempt 1 are logged for the notification id$")]
    public async Task Then1QueuedEntryAnd1DeliveredEntryOnAttempt1AreLogged()
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        var delivered = await teams.DeliveryAsync(notificationId, 1, TimeSpan.FromSeconds(15));
        delivered.Value("Duration").ShouldNotBeNullOrWhiteSpace();
        teams.EntriesFor(DeliveredEvent, notificationId).ShouldHaveSingleItem();
        teams.EntriesFor(AttemptFailedEvent, notificationId).ShouldBeEmpty();
        var queued = teams.QueuedEntry(notificationId);
        queued.Value("QueueLength").ShouldNotBeNullOrWhiteSpace();
        queued.Value("Channel").ShouldBe("teams");
    }

    [Then(@"^both entries carry the trace id of the call$")]
    public async Task ThenBothEntriesCarryTheTraceIdOfTheCall()
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        var queued = teams.QueuedEntry(notificationId);
        queued.Value("TraceId").ShouldNotBeNull().ShouldMatch($"^00-{teams.TraceId}-[0-9a-f]{{16}}-01$");
        var delivered = await teams.DeliveryAsync(notificationId, 1, TimeSpan.FromSeconds(15));
        delivered.Value("TraceId").ShouldBe(queued.Value("TraceId"));

        // One trace covers acceptance and delivery: the delivery worker's activity links to the call's trace.
        var linked = await TeamsScenario.EventuallyAsync(
            () => Task.FromResult(teams.DeliveryActivities().FirstOrDefault(a => a.Links.Any(l => l.Context.TraceId == teams.TraceId))),
            TimeSpan.FromSeconds(5),
            $"no {ActivitySourceName} activity links to the call's trace");
        linked.TraceId.ShouldNotBe(default);
    }

    [Then(@"^the accepted count for ""([^""]*)"" with outcome ""([^""]*)"" and the delivered count for ""([^""]*)"" each rose by 1$")]
    public async Task ThenTheAcceptedAndDeliveredCountsEachRoseBy1(string channel, string outcome, string deliveredChannel)
    {
        await teams.DeliveryAsync(NotificationIdOf(scenario.LastAnswer), 1, TimeSpan.FromSeconds(15));
        scenario.Metrics.Sum(AcceptedCounter, ("channel", channel), ("outcome", outcome)).ShouldBe(1);
        scenario.Metrics.Sum(AcceptedCounter).ShouldBe(1);
        scenario.Metrics.Sum(DeliveredCounter, ("channel", deliveredChannel)).ShouldBe(1);
        scenario.Metrics.Sum(DeliveredCounter).ShouldBe(1);
    }

    [Then(@"^1 delivery duration is recorded for ""([^""]*)""$")]
    public async Task Then1DeliveryDurationIsRecordedFor(string channel)
    {
        await teams.DeliveryAsync(NotificationIdOf(scenario.LastAnswer), 1, TimeSpan.FromSeconds(15));
        var duration = scenario.Metrics.Measurements.Where(m => m.Instrument == DurationHistogram).ShouldHaveSingleItem();
        duration.Tags["channel"].ShouldBe(channel);
        duration.Value.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Then(@"^1 delivered entry on attempt 1 is logged for the notification id$")]
    public async Task Then1DeliveredEntryOnAttempt1IsLogged()
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        await teams.DeliveryAsync(notificationId, 1, TimeSpan.FromSeconds(15));
        teams.EntriesFor(DeliveredEvent, notificationId).ShouldHaveSingleItem();
        teams.EntriesFor(AttemptFailedEvent, notificationId).ShouldBeEmpty();
        teams.Posts.ShouldHaveSingleItem();
    }

    [Then(@"^the queued entry names the channel ""([^""]*)""$")]
    public void ThenTheQueuedEntryNamesTheChannel(string channel) =>
        teams.QueuedEntry(NotificationIdOf(scenario.LastAnswer)).Value("Channel").ShouldBe(channel);

    [Then(@"^no log entry, metric, trace or kept idempotency record holds ""([^""]*)"", ""([^""]*)"", ""([^""]*)"" or ""([^""]*)""$")]
    public async Task ThenNoLogEntryMetricTraceOrKeptIdempotencyRecordHolds(string first, string second, string third, string fourth)
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        await teams.DeliveryAsync(notificationId, 1, TimeSpan.FromSeconds(15));
        // The values were in play: the card carried the customer and the account, and the URL posted to the signature.
        var post = teams.Posts.ShouldHaveSingleItem();
        post.Body.ShouldContain(first);
        post.Body.ShouldContain(second);
        teams.Configuration()[$"{Teams}:Destinations:{third}:WebhookUrl"].ShouldNotBeNull().ShouldEndWith($"&sig={fourth}");

        // Each place holds something, so finding none of the values there means something.
        var logged = scenario.AllLoggedText();
        logged.ShouldNotBeEmpty();
        await TeamsScenario.EventuallyAsync(
            () => Task.FromResult(teams.DeliveryActivities().FirstOrDefault()),
            TimeSpan.FromSeconds(5),
            $"no {ActivitySourceName} activity was recorded");
        var traced = teams.Traces.AllText();
        var kept = await RedisServer.DumpAsync();
        kept.ShouldNotBeEmpty("the call's idempotency record is kept");
        var tagged = scenario.Metrics.Measurements.SelectMany(m => m.Tags.Values).OfType<string>().ToArray();
        tagged.ShouldNotBeEmpty();

        foreach (var value in new[] { first, second, third, fourth })
        {
            logged.ShouldAllBe(text => !text.Contains(value, StringComparison.Ordinal), $"a log entry holds {value}");
            traced.ShouldAllBe(text => !text.Contains(value, StringComparison.Ordinal), $"a trace holds {value}");
            tagged.ShouldAllBe(text => !text.Contains(value, StringComparison.Ordinal), $"a metric holds {value}");
            kept.ShouldAllBe(
                record => !record.Key.Contains(value, StringComparison.Ordinal) && !record.Value.Contains(value, StringComparison.Ordinal),
                $"an idempotency record holds {value}");
        }
    }

    [Then(@"^the attempt failure entry holds the status code (\d+)$")]
    public async Task ThenTheAttemptFailureEntryHoldsTheStatusCode(string code)
    {
        var failure = await teams.AttemptFailureAsync(NotificationIdOf(scenario.LastAnswer), 1, TimeSpan.FromSeconds(15));
        failure.Value("ReplyCode").ShouldBe(code);
        failure.Value("FailureKind").ShouldBe("permanent");
        // The webhook did give its answer text to the service, on the post that carried the value.
        teams.Posts.ShouldHaveSingleItem().Body.ShouldContain(teams.LastCustomer);
    }

    [Then(@"^no log entry holds ""([^""]*)""$")]
    public async Task ThenNoLogEntryHolds(string value)
    {
        await teams.EntryAsync(FailedEvent, NotificationIdOf(scenario.LastAnswer), _ => true, TimeSpan.FromSeconds(15));
        var logged = scenario.AllLoggedText();
        logged.ShouldNotBeEmpty();
        logged.ShouldAllBe(text => !text.Contains(value, StringComparison.Ordinal), $"a log entry holds {value}");
    }

    #endregion

    #region Then — attempts, retries and waits

    [Then(@"^attempt 1 is logged as a ""(transient|permanent)"" failure with (the status code \d+|no status code)$")]
    public async Task ThenAttempt1IsLoggedAsAFailureWith(string kind, string code)
    {
        var failure = await teams.AttemptFailureAsync(NotificationIdOf(scenario.LastAnswer), 1, TimeSpan.FromSeconds(30));
        failure.Value("FailureKind").ShouldBe(kind);
        failure.Value("ReplyCode").ShouldBe(code == "no status code" ? string.Empty : code["the status code ".Length..]);
    }

    [Then(@"^the card is posted on attempt 2$")]
    public async Task ThenTheCardIsPostedOnAttempt2() => await ThenTheCardIsPostedOnAttempt2And(null);

    [Then(@"^the card is posted on attempt 2, (.+)$")]
    public async Task ThenTheCardIsPostedOnAttempt2And(string? more)
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        var attempt1 = await teams.AttemptFailureAsync(notificationId, 1, TimeSpan.FromSeconds(30));
        attempt1.Value("FailureKind").ShouldBe("transient");
        if (teams.ClearFault is not null)
        {
            await teams.ClearFault();
        }

        var delivered = await teams.DeliveryAsync(notificationId, 2, TimeSpan.FromSeconds(45));
        teams.EntriesFor(AttemptFailedEvent, notificationId).ShouldHaveSingleItem().Value("Attempt").ShouldBe("1");
        teams.EntriesFor(FailedEvent, notificationId).ShouldBeEmpty();
        TextBlocksOf(teams.Posts[^1]).ShouldNotBeEmpty();
        var sinceAttempt1 = delivered.LoggedAt - attempt1.LoggedAt;

        Match match;
        if (string.IsNullOrEmpty(more))
        {
            return;
        }

        if ((match = Regex.Match(more, @"^no sooner than (\d+) seconds and sooner than (\d+) seconds after attempt 1$")).Success)
        {
            sinceAttempt1.ShouldBeGreaterThanOrEqualTo(Seconds(match.Groups[1]));
            sinceAttempt1.ShouldBeLessThan(Seconds(match.Groups[2]));
        }
        else if ((match = Regex.Match(more, @"^no sooner than (\d+) seconds after attempt 1$")).Success)
        {
            sinceAttempt1.ShouldBeGreaterThanOrEqualTo(Seconds(match.Groups[1]));
        }
        else if ((match = Regex.Match(more, @"^and the webhook stub holds (\d+) posts$")).Success)
        {
            teams.Posts.Count.ShouldBe(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(more), more, "no such outcome in the spec");
        }
    }

    [Then(@"^the notification fails with no attempt 2$")]
    public async Task ThenTheNotificationFailsWithNoAttempt2() => await ThenTheNotificationFailsWithNoAttempt2And(null);

    [Then(@"^the notification fails with no attempt 2, (.+)$")]
    public async Task ThenTheNotificationFailsWithNoAttempt2And(string? more)
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        var failed = await teams.EntryAsync(FailedEvent, notificationId, _ => true, TimeSpan.FromSeconds(30));
        failed.Value("AttemptCount").ShouldBe("1");
        failed.Value("ReplyCode").ShouldBe(teams.ExpectedCode);

        await Task.Delay(PastAttempt2);
        teams.EntriesFor(AttemptFailedEvent, notificationId).ShouldHaveSingleItem().Value("Attempt").ShouldBe("1");
        teams.EntriesFor(DeliveredEvent, notificationId).ShouldBeEmpty();
        teams.Posts.ShouldHaveSingleItem();
        scenario.Metrics.Sum(FailedCounter, ("channel", "teams")).ShouldBe(1);

        switch (more)
        {
            case null or "":
                break;
            case "and \"https://elsewhere.example\" receives nothing":
                teams.Elsewhere.IsRunning.ShouldBeTrue("the elsewhere stub must be ready to receive");
                teams.Elsewhere.Requests.ShouldBeEmpty();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(more), more, "no such outcome in the spec");
        }
    }

    [Then(@"^(\d+) attempts are made, with (\d+) posts in all$")]
    public async Task ThenAttemptsAreMadeWithPostsInAll(int attempts, int posts)
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        await teams.EntryAsync(FailedEvent, notificationId, _ => true, TimeSpan.FromSeconds(60));
        await Task.Delay(TimeSpan.FromSeconds(2));
        teams.EntriesFor(AttemptFailedEvent, notificationId).Length.ShouldBe(attempts);
        teams.Posts.Count.ShouldBe(posts);
    }

    [Then(@"^(\d+) attempt failures and 1 failure error with attempt count (\d+) are logged$")]
    public async Task ThenAttemptFailuresAnd1FailureErrorAreLogged(int failures, int attemptCount)
    {
        var notificationId = NotificationIdOf(scenario.LastAnswer);
        var failed = await teams.EntryAsync(FailedEvent, notificationId, _ => true, TimeSpan.FromSeconds(60));
        failed.Value("AttemptCount").ShouldBe(Text(attemptCount));
        failed.Value("ReplyCode").ShouldBe(teams.ExpectedCode);
        teams.EntriesFor(FailedEvent, notificationId).ShouldHaveSingleItem();

        var attempts = teams.EntriesFor(AttemptFailedEvent, notificationId).OrderBy(e => e.LoggedAt).ToArray();
        attempts.Select(e => e.Value("Attempt")).ShouldBe(Enumerable.Range(1, failures).Select(Text));
        attempts.ShouldAllBe(e => e.Value("FailureKind") == "transient" && e.Value("ReplyCode") == teams.ExpectedCode);
        teams.EntriesFor(DeliveredEvent, notificationId).ShouldBeEmpty();
    }

    [Then(@"^the failed count for ""([^""]*)"" rose by 1$")]
    public async Task ThenTheFailedCountRoseBy1(string channel)
    {
        await teams.EntryAsync(FailedEvent, NotificationIdOf(scenario.LastAnswer), _ => true, TimeSpan.FromSeconds(60));
        scenario.Metrics.Sum(FailedCounter, ("channel", channel)).ShouldBe(1);
        scenario.Metrics.Sum(FailedCounter).ShouldBe(1);
        scenario.Metrics.Sum(DeliveredCounter).ShouldBe(0);
    }

    [Then(@"^the mail to ""([^""]*)"" is sent before the card for ""([^""]*)"" is posted$")]
    public async Task ThenTheMailIsSentBeforeTheCardIsPosted(string to, string customer)
    {
        var mail = await teams.EntryAsync(DeliveredEvent, teams.NotificationOf(to), _ => true, TimeSpan.FromSeconds(15));
        var card = await teams.EntryAsync(DeliveredEvent, teams.NotificationOf(customer), _ => true, TimeSpan.FromSeconds(30));
        mail.Value("Channel").ShouldBe("email");
        card.Value("Channel").ShouldBe("teams");
        mail.LoggedAt.ShouldBeLessThan(card.LoggedAt);

        // At the webhook stub: the card's post of attempt 1, then, after the mail, its post of attempt 2.
        var posts = teams.Posts;
        posts.Count.ShouldBe(2);
        posts.ShouldAllBe(p => p.Body.Contains(customer));
        posts[1].ReceivedAt.ShouldBeGreaterThan(mail.LoggedAt);
    }

    [Then(@"^the card for ""([^""]*)"" is posted on attempt (\d+)$")]
    public async Task ThenTheCardForIsPostedOnAttempt(string customer, int attempt)
    {
        var notificationId = teams.NotificationOf(customer);
        await teams.DeliveryAsync(notificationId, attempt, TimeSpan.FromSeconds(30));
        var failure = teams.EntriesFor(AttemptFailedEvent, notificationId).ShouldHaveSingleItem();
        failure.Value("FailureKind").ShouldBe("transient");
        failure.Value("ReplyCode").ShouldBe(teams.ExpectedCode);
    }

    #endregion

    /// <summary>The one post of the scenario, once the webhook stub got it and a little longer for a stray one.</summary>
    private async Task<RecordingHttpStub.Request> SinglePostAsync() => (await teams.PostsAsync(1)).ShouldHaveSingleItem();

    private static TimeSpan Seconds(Group group) =>
        TimeSpan.FromSeconds(int.Parse(group.Value, System.Globalization.CultureInfo.InvariantCulture));
}
