using DKNet.Notification.App.TestSupport;
using Microsoft.AspNetCore.Hosting;

namespace DKNet.Notification.App.Tests.Integration.Notifications;

/// <summary>
/// DRK-2020 §5 outline "A bad delivery setting stops the start-up", row "a queue size of 0", through the real host
/// start; every row runs at the settings check in <c>Unit/Notifications/DeliverySettingsTests</c>. The setting goes
/// in through <c>UseSetting</c>: the host reads its settings once, at registration, before <c>Build()</c>.
/// </summary>
public sealed class BadDeliverySettingStartupTests
{
    [Fact(DisplayName = "A bad delivery setting stops the start-up, through the host")]
    public void A_bad_delivery_setting_stops_the_start_up_through_the_host()
    {
        using var factory = new BadDeliveryApiFactory();

        var error = Should.Throw<Exception>(() => factory.Services);

        var refusal = Chain(error).OfType<InvalidOperationException>()
            .FirstOrDefault(e => e.Message.Contains("Notifications:Delivery:QueueCapacity", StringComparison.Ordinal));
        refusal.ShouldNotBeNull($"the start-up must fail on the queue size, but failed with: {error}");
        refusal.Message.ShouldNotMatch(@"(?<![0-9])0(?![0-9])");
    }

    private static IEnumerable<Exception> Chain(Exception error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private sealed class BadDeliveryApiFactory : TestApiFactoryBase
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Notifications:Delivery:QueueCapacity", "0");
        }
    }
}
