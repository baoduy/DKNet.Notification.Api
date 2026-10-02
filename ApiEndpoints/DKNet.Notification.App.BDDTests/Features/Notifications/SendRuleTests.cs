using System.Text.Json.Nodes;
using DKNet.Notification.App.BDDTests.Features.Notifications.Steps;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.SendScenario;

namespace DKNet.Notification.App.BDDTests.Features.Notifications;

/// <summary>
/// DRK-2013 rules the §5 Gherkin does not reach on its own (brief DRK-2016 §6), on the same host as the send
/// scenarios: a bind failure holds no key (R2), and no idempotency record or counter holds a parameter value or
/// a recipient (R8). Every expected value is a literal from the spec.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SendRuleTests
{
    private SendScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _scenario = new SendScenario();
        await _scenario.StartAsync(signIn: true, withRedis: true);
        _scenario.AllowCaller("treasury-ops");
    }

    [TearDown]
    public async Task TearDown() => await _scenario.DisposeAsync();

    [TestCase("this is not JSON", TestName = "A body that is not JSON holds no key")]
    [TestCase(
        """{"channel":"email","templateId":"account-opened","parameters":{"to":"jane@example.com","amount":100}}""",
        TestName = "A parameter value that is not a string holds no key")]
    public async Task A_body_that_cannot_be_bound_holds_no_key(string body)
    {
        ShouldBeRefusedWith(await _scenario.SendAsync("treasury-ops", "k-9001", body), "INVALID_REQUEST");

        var fixedCall = await _scenario.SendAsync("treasury-ops", "k-9001", EmailBody("account-opened", "jane@example.com"));

        _scenario.ShouldBeAccepted(fixedCall);
    }

    [Test]
    public async Task No_idempotency_record_or_counter_holds_a_parameter_value_or_the_recipient()
    {
        string[] personalData = ["jane@example.com", "Jane Tan", "0012345678"];
        var answer = await _scenario.SendAsync("treasury-ops", "k-9002", Body("email", "account-opened", new JsonObject
        {
            ["to"] = personalData[0],
            ["customerName"] = personalData[1],
            ["accountNumber"] = personalData[2]
        }));
        var notificationId = _scenario.ShouldBeAccepted(answer);

        var records = await RedisServer.DumpAsync();
        // The kept 202 is in the store, so the search below runs over a real record.
        records.Values.ShouldContain(value => value.Contains(notificationId, StringComparison.OrdinalIgnoreCase));
        _scenario.Metrics.Measurements.ShouldNotBeEmpty();
        var tags = _scenario.Metrics.Measurements.SelectMany(m => m.Tags.Values).OfType<string>().ToArray();

        foreach (var value in personalData)
        {
            records.Keys.ShouldAllBe(key => !key.Contains(value, StringComparison.OrdinalIgnoreCase));
            records.Values.ShouldAllBe(record => !record.Contains(value, StringComparison.OrdinalIgnoreCase));
            tags.ShouldAllBe(tag => !tag.Contains(value, StringComparison.OrdinalIgnoreCase));
        }
    }
}
