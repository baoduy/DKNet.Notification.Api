using System.Text.RegularExpressions;
using DKNet.Notification.AppServices.Delivery;
using Microsoft.Extensions.Configuration;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>
/// DRK-2020 §5 outline "A bad delivery setting stops the start-up" (@unit), one row per case. The settings are
/// bound from a settings source the way the start-up binds them (<c>Get&lt;T&gt;()</c>), then checked. The setting
/// names are the leader's contract names (brief DRK-2025 §5); every other expected value is a literal from the spec.
/// One row also runs through the real host start: <c>Integration/Notifications/BadDeliverySettingStartupTests</c>.
/// </summary>
public sealed class DeliverySettingsTests
{
    private static DeliverySettings Bind(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build()
            .GetSection("Notifications:Delivery")
            .Get<DeliverySettings>() ?? new DeliverySettings();

    public static TheoryData<string, Dictionary<string, string?>, string, string[]> BadSettings() => new()
    {
        {
            "a queue size of 0",
            new() { ["Notifications:Delivery:QueueCapacity"] = "0" },
            "Notifications:Delivery:QueueCapacity",
            ["0"]
        },
        {
            "4 attempts",
            new() { ["Notifications:Delivery:MaxAttempts"] = "4" },
            "Notifications:Delivery:MaxAttempts",
            ["4"]
        },
        {
            "a wait of 301 seconds",
            new()
            {
                ["Notifications:Delivery:RetryDelaysSeconds:0"] = "5",
                ["Notifications:Delivery:RetryDelaysSeconds:1"] = "301"
            },
            "Notifications:Delivery:RetryDelaysSeconds",
            ["301"]
        },
        {
            "3 waits",
            new()
            {
                ["Notifications:Delivery:RetryDelaysSeconds:0"] = "5",
                ["Notifications:Delivery:RetryDelaysSeconds:1"] = "30",
                ["Notifications:Delivery:RetryDelaysSeconds:2"] = "60"
            },
            "Notifications:Delivery:RetryDelaysSeconds",
            ["5", "30", "60"]
        }
    };

    [Theory(DisplayName = "A bad delivery setting stops the start-up")]
    [MemberData(nameof(BadSettings))]
    public void A_bad_delivery_setting_stops_the_start_up(
        string fault,
        Dictionary<string, string?> settings,
        string setting,
        string[] values)
    {
        var delivery = Bind(settings);

        var refusal = Should.Throw<InvalidOperationException>(delivery.Validate, fault);

        refusal.Message.ShouldContain(setting, Case.Sensitive, $"the refusal must name {setting}");
        foreach (var value in values)
        {
            // A whole number, so "0" is not found inside a rule such as "1 to 100000".
            Regex.IsMatch(refusal.Message, $@"(?<![0-9]){Regex.Escape(value)}(?![0-9])")
                .ShouldBeFalse($"the refusal holds the value {value}: {refusal.Message}");
        }
    }

    [Fact(DisplayName = "The default delivery settings let the service start")]
    public void The_default_delivery_settings_let_the_service_start()
    {
        var delivery = Bind([]);

        delivery.QueueCapacity.ShouldBe(1000);
        delivery.MaxAttempts.ShouldBe(3);
        delivery.RetryDelaysSeconds.ShouldBe([5, 30]);
        Should.NotThrow(delivery.Validate);
    }

    [Fact(DisplayName = "Delivery settings inside every rule let the service start")]
    public void Delivery_settings_inside_every_rule_let_the_service_start()
    {
        var delivery = Bind(new Dictionary<string, string?>
        {
            ["Notifications:Delivery:QueueCapacity"] = "100000",
            ["Notifications:Delivery:MaxAttempts"] = "1",
            ["Notifications:Delivery:RetryDelaysSeconds:0"] = "1",
            ["Notifications:Delivery:RetryDelaysSeconds:1"] = "300"
        });

        // The 2 waits replace the defaults; they are not added to them.
        delivery.RetryDelaysSeconds.ShouldBe([1, 300]);
        Should.NotThrow(delivery.Validate);
    }
}
