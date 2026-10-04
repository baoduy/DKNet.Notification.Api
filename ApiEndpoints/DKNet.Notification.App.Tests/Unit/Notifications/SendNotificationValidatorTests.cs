using DKNet.Notification.AppServices.Notifications;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>DRK-2013 §3a field rules of a send call: every refusal carries INVALID_REQUEST.</summary>
public sealed class SendNotificationValidatorTests
{
    private static readonly SendNotificationValidator Validator = new();

    private static Dictionary<string, string> Recipient() => new(StringComparer.Ordinal) { ["to"] = "jane@example.com" };

    private static SendNotificationRequest Valid() => new("email", "account-opened", Recipient());

    private static void ShouldPass(SendNotificationRequest request) =>
        Validator.Validate(request).Errors.ShouldBeEmpty();

    private static void ShouldFail(SendNotificationRequest request)
    {
        var errors = Validator.Validate(request).Errors;
        errors.ShouldNotBeEmpty();
        errors.ShouldAllBe(e => e.ErrorCode == "INVALID_REQUEST");
    }

    private static Dictionary<string, string> Parameters(int count)
    {
        var parameters = Recipient();
        for (var i = 1; i < count; i++)
        {
            parameters[$"p{i}"] = "value";
        }

        return parameters;
    }

    [Fact]
    public void A_valid_call_passes() => ShouldPass(Valid());

    [Fact]
    public void An_empty_parameter_list_passes() => ShouldPass(Valid() with { Parameters = new Dictionary<string, string>() });

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public void A_channel_of_any_text_from_1_to_50_characters_passes(int length) =>
        ShouldPass(Valid() with { Channel = new string('c', length) });

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void A_template_id_from_1_to_100_characters_passes(int length) =>
        ShouldPass(Valid() with { TemplateId = new string('t', length) });

    [Fact]
    public void Fifty_parameters_pass() => ShouldPass(Valid() with { Parameters = Parameters(50) });

    [Fact]
    public void A_parameter_name_of_64_letters_digits_or_underscores_passes() =>
        ShouldPass(Valid() with { Parameters = new Dictionary<string, string> { [new string('a', 62) + "_9"] = "x" } });

    [Fact]
    public void A_parameter_value_of_4000_characters_passes() =>
        ShouldPass(Valid() with { Parameters = new Dictionary<string, string> { ["customerName"] = new string('x', 4000) } });

    [Fact]
    public void No_channel_is_refused() => ShouldFail(Valid() with { Channel = null! });

    [Fact]
    public void An_empty_channel_is_refused() => ShouldFail(Valid() with { Channel = "" });

    [Fact]
    public void A_channel_of_51_characters_is_refused() => ShouldFail(Valid() with { Channel = new string('c', 51) });

    [Fact]
    public void No_template_id_is_refused() => ShouldFail(Valid() with { TemplateId = null! });

    [Fact]
    public void An_empty_template_id_is_refused() => ShouldFail(Valid() with { TemplateId = "" });

    [Fact]
    public void A_template_id_of_101_characters_is_refused() => ShouldFail(Valid() with { TemplateId = new string('t', 101) });

    [Fact]
    public void No_parameter_list_is_refused()
    {
        var error = Validator.Validate(Valid() with { Parameters = null! }).Errors.ShouldHaveSingleItem();
        error.ErrorCode.ShouldBe("INVALID_REQUEST");
        error.PropertyName.ShouldBe("Parameters");
    }

    [Fact]
    public void Fifty_one_parameters_are_refused()
    {
        var error = Validator.Validate(Valid() with { Parameters = Parameters(51) }).Errors.ShouldHaveSingleItem();
        error.ErrorCode.ShouldBe("INVALID_REQUEST");
        error.ErrorMessage.ShouldBe("'Parameters' must hold at most 50 entries.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("customer-name")]
    [InlineData("customer name")]
    [InlineData("name\n")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void A_parameter_name_outside_its_rule_is_refused(string name)
    {
        var error = Validator.Validate(Valid() with { Parameters = new Dictionary<string, string> { [name] = "x" } })
            .Errors.ShouldHaveSingleItem();
        error.ErrorCode.ShouldBe("INVALID_REQUEST");
        error.ErrorMessage.ShouldBe("Each parameter name must be 1 to 64 letters, digits or '_'.");
    }

    [Fact]
    public void A_parameter_value_that_is_null_is_refused()
    {
        var error = Validator.Validate(Valid() with { Parameters = new Dictionary<string, string> { ["to"] = null! } })
            .Errors.ShouldHaveSingleItem();
        error.ErrorCode.ShouldBe("INVALID_REQUEST");
        error.ErrorMessage.ShouldBe("Each parameter value must be a string.");
    }

    [Fact]
    public void A_parameter_value_of_4001_characters_is_refused()
    {
        var error = Validator.Validate(Valid() with { Parameters = new Dictionary<string, string> { ["customerName"] = new string('x', 4001) } })
            .Errors.ShouldHaveSingleItem();
        error.ErrorCode.ShouldBe("INVALID_REQUEST");
        error.ErrorMessage.ShouldBe("Each parameter value must be at most 4,000 characters.");
    }

    [Fact]
    public void No_message_echoes_a_value_the_caller_sent()
    {
        string[] sent = [new string('c', 51), new string('t', 101), "customer-name", new string('x', 4001)];
        var request = new SendNotificationRequest(sent[0], sent[1], new Dictionary<string, string> { [sent[2]] = sent[3] });

        var errors = Validator.Validate(request).Errors;
        errors.Count.ShouldBe(4);
        errors.ShouldAllBe(e => sent.All(value => !e.ErrorMessage.Contains(value)));
    }
}
