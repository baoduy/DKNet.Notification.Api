using DKNet.Notification.App.TestSupport;
using Microsoft.AspNetCore.Hosting;

namespace DKNet.Notification.App.Tests.Integration.Notifications;

/// <summary>
/// DRK-2020 §5 outline "A bad delivery setting stops the start-up", row "a queue size of 0", and the status retention
/// rule (spec §9), through the real host start; every delivery row runs at the settings check in
/// <c>Unit/Notifications/DeliverySettingsTests</c>. The setting goes in through <c>UseSetting</c>: the host reads its
/// settings once, at registration, before <c>Build()</c>.
/// </summary>
public sealed class BadDeliverySettingStartupTests
{
    [Fact(DisplayName = "A bad delivery setting stops the start-up, through the host")]
    public void A_bad_delivery_setting_stops_the_start_up_through_the_host() =>
        StartWith("Notifications:Delivery:QueueCapacity", "0")
            .Message.ShouldNotMatch(@"(?<![0-9])0(?![0-9])");

    [Theory]
    [InlineData("0")]
    [InlineData("169")]
    public void A_status_retention_outside_its_rule_stops_the_host(string hours) =>
        StartWith("Notifications:Status:RetentionHours", hours)
            .Message.ShouldBe("The Notifications:Status:RetentionHours setting must be from one to one hundred sixty-eight hours.");

    /// <summary>Starts the host with one setting and returns the refusal that names it.</summary>
    private static InvalidOperationException StartWith(string key, string value)
    {
        using var factory = new SettingApiFactory(key, value);

        var error = Should.Throw<Exception>(() => factory.Services);

        var refusal = Chain(error).OfType<InvalidOperationException>()
            .FirstOrDefault(e => e.Message.Contains(key, StringComparison.Ordinal));
        return refusal.ShouldNotBeNull($"the start-up must fail on {key}, but failed with: {error}");
    }

    private static IEnumerable<Exception> Chain(Exception error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private sealed class SettingApiFactory(string key, string value) : TestApiFactoryBase
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting(key, value);
        }
    }
}
