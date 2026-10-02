using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using static DKNet.Notification.App.BDDTests.Features.Notifications.Steps.SendScenario;

namespace DKNet.Notification.App.BDDTests.Features.Notifications.Steps;

/// <summary>
/// Given and When steps for <c>SendNotification.feature</c> (DRK-2013 §5). Every expected value is a literal from
/// the spec. Scoped to the feature, so its step texts never clash with the start-up feature's.
/// </summary>
[Binding]
[Scope(Feature = FeatureTitle)]
public sealed class SendNotificationSteps(SendScenario scenario)
{
    public const string FeatureTitle = "Send API: the send endpoint, sign-in, idempotency and skip";

    [AfterScenario]
    public async Task AfterScenario() => await scenario.DisposeAsync();

    #region Given — the service

    [Given(@"^the service runs with its released template catalogue and sign-in on$")]
    [Given(@"^the service started with its released template catalogue and sign-in on$")]
    public async Task GivenTheServiceRunsWithSignInOn() => await scenario.StartAsync(signIn: true, withRedis: true);

    [Given(@"^a developer runs the service locally with its released template catalogue and sign-in off$")]
    public async Task GivenADeveloperRunsTheServiceLocally() =>
        await scenario.StartAsync(signIn: false, withRedis: false, environment: "Development");

    [Given(@"^the template settings are changed to remove ""([^""]*)"" while the service runs$")]
    public void GivenTheTemplateSettingsAreChangedToRemove(string templateId)
    {
        var configuration = scenario.Factory.Services.GetRequiredService<IConfiguration>();
        configuration["Notifications:Templates:0:TemplateId"].ShouldBe(templateId);

        scenario.Factory.Settings.Change("Notifications:Templates:0:TemplateId", "account-retired");

        // The change landed: the settings no longer register the template.
        configuration.GetSection("Notifications:Templates").GetChildren()
            .Select(template => template["TemplateId"])
            .ShouldBe(["account-retired"]);
    }

    #endregion

    #region Given — callers and tokens

    [Given(@"^""([^""]*)"" is a caller allowed to send notifications$")]
    [Given(@"^""([^""]*)"" is also a caller allowed to send notifications$")]
    public void GivenIsACallerAllowedToSendNotifications(string caller) => scenario.AllowCaller(caller);

    [Given(@"^""([^""]*)"" holds a token with ""([^""]*)"" in its ""([^""]*)"" claim$")]
    public void GivenHoldsATokenWithThePermissionInItsClaim(string caller, string permission, string claim)
    {
        // scp and scope carry one space-separated list; roles carry one value per claim.
        var permissionClaims = claim switch
        {
            "scp" or "scope" => $"{claim}=user.read {permission}",
            "roles" => $"roles=notifications.read;roles={permission}",
            _ => throw new ArgumentOutOfRangeException(nameof(claim), claim, "no such claim in the spec")
        };
        scenario.Callers[caller] = new Credential("Bearer card-ops-token", $"client_id={caller};{permissionClaims}");
    }

    [Given(@"^""([^""]*)"" calls with (.+)$")]
    public void GivenCallsWith(string caller, string credential) =>
        scenario.Callers[caller] = credential switch
        {
            "no token" => new Credential(null, $"client_id={caller};roles={Permission}"),
            "an invalid token" => new Credential($"Bearer {TestAuthHandler.InvalidToken}", $"client_id={caller};roles={Permission}"),
            "a token that names no calling application" => new Credential("Bearer card-ops-token", $"roles={Permission}"),
            "a token without the permission" => new Credential("Bearer card-ops-token", $"client_id={caller};scp=notifications.read"),
            _ => throw new ArgumentOutOfRangeException(nameof(credential), credential, "no such credential in the spec")
        };

    [Given(@"^""([^""]*)"" holds a token with the permission, ""([^""]*)"" in its ""([^""]*)"" claim and ""([^""]*)"" in its ""([^""]*)"" claim$")]
    public void GivenHoldsATokenWithTwoCallerClaims(
        string caller,
        string first,
        string firstClaim,
        string second,
        string secondClaim) =>
        // The second claim comes first in the token, so only the claim order of the spec can pick the first.
        scenario.Callers[caller] = new Credential(
            SharedToken,
            $"{secondClaim}={second};{firstClaim}={first};roles={Permission}");

    #endregion

    #region Given — earlier calls

    [Given(@"^""([^""]*)"" emailed template ""([^""]*)"" to ""([^""]*)"" with key ""([^""]*)""$")]
    public async Task GivenEmailedTemplateToWithKey(string caller, string templateId, string to, string key) =>
        scenario.ShouldBeAccepted(await scenario.SendAsync(caller, key, EmailBody(templateId, to)));

    [Given(@"^""([^""]*)"" emailed template ""([^""]*)"" with key ""([^""]*)"" on its first token$")]
    public async Task GivenEmailedTemplateWithKeyOnItsFirstToken(string caller, string templateId, string key)
    {
        scenario.Callers[caller] = scenario.Callers[caller] with { Authorization = "Bearer first-token" };
        scenario.ShouldBeAccepted(await scenario.SendAsync(caller, key, EmailBody(templateId, "jane@example.com")));
    }

    [Given(@"^""([^""]*)"" emailed template ""([^""]*)"" with key ""([^""]*)""$")]
    public async Task GivenEmailedTemplateWithKey(string caller, string templateId, string key) =>
        scenario.ShouldBeAccepted(await scenario.SendAsync(caller, key, EmailBody(templateId, "jane@example.com")));

    [Given(@"^the service is still working on that call$")]
    public void GivenTheServiceIsStillWorkingOnThatCall() =>
        scenario.HeldCall.ShouldNotBeNull().IsCompleted.ShouldBeFalse();

    [Given(@"^""([^""]*)"" sent template ""([^""]*)"" with key ""([^""]*)"" and was refused with ""TEMPLATE_NOT_FOUND""$")]
    public async Task GivenSentTemplateAndWasRefusedWithTemplateNotFound(string caller, string templateId, string key) =>
        ShouldBeRefusedWith(
            await scenario.SendAsync(caller, key, EmailBody(templateId, "jane@example.com")),
            "TEMPLATE_NOT_FOUND");

    [Given(@"^""([^""]*)"" sent a call with an empty channel and key ""([^""]*)"" and was refused with ""INVALID_REQUEST""$")]
    public async Task GivenSentACallWithAnEmptyChannelAndWasRefused(string caller, string key) =>
        ShouldBeRefusedWith(
            await scenario.SendAsync(caller, key, Body("", "account-opened", new JsonObject { ["to"] = "jane@example.com" })),
            "INVALID_REQUEST");

    [Given(@"^""([^""]*)"" sent a call whose body is 65,537 bytes and key ""([^""]*)"" and was refused with status 413$")]
    public async Task GivenSentAnOversizedCallAndWasRefused(string caller, string key) =>
        // Sent with no Content-Length: the limit must hold on the bytes read, not only on the declared size.
        (await scenario.SendAsync(caller, key, JsonOfUnknownLength(BodyOfSize(65_537))))
        .Status.ShouldBe(HttpStatusCode.RequestEntityTooLarge);

    #endregion

    #region When — valid calls

    [When(@"^""([^""]*)"" asks to email template ""([^""]*)"" to ""([^""]*)"" with key ""([^""]*)""$")]
    public async Task WhenAsksToEmailTemplateToWithKey(string caller, string templateId, string to, string key) =>
        await scenario.SendAsync(caller, key, EmailBody(templateId, to));

    [When(@"^""([^""]*)"" sends template ""([^""]*)"" on channel ""([^""]*)""$")]
    public async Task WhenSendsTemplateOnChannel(string caller, string templateId, string channel) =>
        await scenario.SendAsync(caller, NewKey(), Body(channel, templateId, new JsonObject { ["to"] = "jane@example.com" }));

    [When(@"^""([^""]*)"" sends template ""([^""]*)"" on channel ""([^""]*)"" with an empty parameter list$")]
    public async Task WhenSendsTemplateOnChannelWithAnEmptyParameterList(string caller, string templateId, string channel) =>
        await scenario.SendAsync(caller, NewKey(), Body(channel, templateId, new JsonObject()));

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" to ""([^""]*)"" for customer ""([^""]*)"" and account ""([^""]*)""$")]
    public async Task WhenEmailsTemplateToForCustomerAndAccount(
        string caller,
        string templateId,
        string to,
        string customerName,
        string accountNumber) =>
        await scenario.SendAsync(caller, NewKey(), Body("email", templateId, new JsonObject
        {
            ["to"] = to,
            ["customerName"] = customerName,
            ["accountNumber"] = accountNumber
        }));

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" to ""([^""]*)""$")]
    public async Task WhenEmailsTemplateTo(string caller, string templateId, string to) =>
        await scenario.SendAsync(caller, NewKey(), EmailBody(templateId, to));

    [When(@"^the developer emails template ""([^""]*)"" to ""([^""]*)"" with key ""([^""]*)""$")]
    public async Task WhenTheDeveloperEmailsTemplateToWithKey(string templateId, string to, string key) =>
        await scenario.SendAsync("developer", key, EmailBody(templateId, to));

    #endregion

    #region When — repeated calls and keys

    [When(@"^""([^""]*)"" sends the same call again with key ""([^""]*)""$")]
    public async Task WhenSendsTheSameCallAgainWithKey(string caller, string key) =>
        await scenario.SendAsync(caller, key, SameCallAs(caller, key));

    [When(@"^""([^""]*)"" sends the same call with key ""([^""]*)"" on a new token$")]
    public async Task WhenSendsTheSameCallWithKeyOnANewToken(string caller, string key)
    {
        var body = SameCallAs(caller, key);
        scenario.Callers[caller] = scenario.Callers[caller] with { Authorization = "Bearer new-token" };
        await scenario.SendAsync(caller, key, body);
    }

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" with key ""([^""]*)""$")]
    [When(@"^""([^""]*)"" emails template ""([^""]*)"" with key ""([^""]*)"" at once$")]
    public async Task WhenEmailsTemplateWithKey(string caller, string templateId, string key) =>
        await scenario.SendAsync(caller, key, EmailBody(templateId, "jane@example.com"));

    [When(@"^""([^""]*)"" sends template ""([^""]*)"" with key ""([^""]*)"" (\d+) seconds later$")]
    public async Task WhenSendsTemplateWithKeySecondsLater(string caller, string templateId, string key, int seconds)
    {
        var elapsed = scenario.SinceFirstCall.ShouldNotBeNull().Elapsed;
        var wait = TimeSpan.FromSeconds(seconds) - elapsed;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait);
        }

        await scenario.SendAsync(caller, key, EmailBody(templateId, "jane@example.com"));
    }

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" with no key$")]
    public async Task WhenEmailsTemplateWithNoKey(string caller, string templateId) =>
        await scenario.SendAsync(caller, null, EmailBody(templateId, "jane@example.com"));

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" with a key of (\d+) characters$")]
    public async Task WhenEmailsTemplateWithAKeyOfCharacters(string caller, string templateId, int length) =>
        await scenario.SendAsync(caller, new string('k', length), EmailBody(templateId, "jane@example.com"));

    [When(@"^""([^""]*)"" emails template ""([^""]*)"" with the key ""([^""]*)""$")]
    public async Task WhenEmailsTemplateWithTheKey(string caller, string templateId, string key) =>
        await scenario.SendAsync(caller, key, EmailBody(templateId, "jane@example.com"));

    #endregion

    #region When — bodies

    [When(@"^""([^""]*)"" sends a call whose body is 65,537 bytes$")]
    public async Task WhenSendsACallWhoseBodyIs65537Bytes(string caller) =>
        await scenario.SendAsync(caller, NewKey(), BodyOfSize(65_537));

    [When(@"^""([^""]*)"" sends a call with (.+)$")]
    public async Task WhenSendsACallWith(string caller, string fault) =>
        await scenario.SendAsync(caller, NewKey(), FaultyBody(fault));

    #endregion

    /// <summary>A fresh, valid key for a call whose step names none.</summary>
    private static string NewKey() => Guid.NewGuid().ToString("N");

    private string SameCallAs(string caller, string key)
    {
        var last = scenario.LastCall.ShouldNotBeNull();
        last.Caller.ShouldBe(caller);
        last.Key.ShouldBe(key);
        return last.Body.ShouldNotBeNull();
    }

    private static string FaultyBody(string fault)
    {
        JsonObject Recipient() => new() { ["to"] = "jane@example.com" };

        return fault switch
        {
            "an empty channel" => Body("", "account-opened", Recipient()),
            "a channel of 51 characters" => Body(new string('c', 51), "account-opened", Recipient()),
            "a template id of 101 characters" => Body("email", new string('t', 101), Recipient()),
            "no parameter list at all" => Body("email", "account-opened", null),
            "the parameter \"amount\" set to the number 100" =>
                Body("email", "account-opened", new JsonObject { ["to"] = "jane@example.com", ["amount"] = 100 }),
            "51 parameters" => Body("email", "account-opened", FiftyOneParameters()),
            "the parameter \"customer-name\"" =>
                Body("email", "account-opened", new JsonObject { ["to"] = "jane@example.com", ["customer-name"] = "Jane" }),
            "a parameter value of 4,001 characters" =>
                Body("email", "account-opened", new JsonObject { ["to"] = "jane@example.com", ["customerName"] = new string('x', 4001) }),
            "a body that is not JSON" => "this is not JSON",
            _ => throw new ArgumentOutOfRangeException(nameof(fault), fault, "no such fault in the spec")
        };
    }

    private static JsonObject FiftyOneParameters()
    {
        var parameters = new JsonObject { ["to"] = "jane@example.com" };
        for (var i = 1; i <= 50; i++)
        {
            parameters[$"p{i:00}"] = "value";
        }

        parameters.Count.ShouldBe(51);
        return parameters;
    }
}
