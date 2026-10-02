using DKNet.Notification.AppServices.Delivery;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>DRK-2020 §3a: the edges of each delivery setting's rule, and the refusal text.</summary>
public sealed class DeliverySettingsRuleTests
{
    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(100_000, 3, 300, 300)]
    public void A_value_at_the_edge_of_its_rule_is_kept(int queueCapacity, int maxAttempts, int wait2, int wait3) =>
        Should.NotThrow(new DeliverySettings(queueCapacity, maxAttempts, [wait2, wait3]).Validate);

    [Theory]
    [InlineData(100_001, 3, 5, 30, "The Notifications:Delivery:QueueCapacity setting must be from one to one hundred thousand.")]
    [InlineData(1000, 0, 5, 30, "The Notifications:Delivery:MaxAttempts setting must be from one to three.")]
    [InlineData(1000, 3, 0, 30, "The Notifications:Delivery:RetryDelaysSeconds setting must hold exactly two waits, each from one to three hundred seconds.")]
    [InlineData(1000, 3, 5, 301, "The Notifications:Delivery:RetryDelaysSeconds setting must hold exactly two waits, each from one to three hundred seconds.")]
    public void A_value_past_the_edge_of_its_rule_stops_the_start_up(
        int queueCapacity,
        int maxAttempts,
        int wait2,
        int wait3,
        string refusal) =>
        Should.Throw<InvalidOperationException>(new DeliverySettings(queueCapacity, maxAttempts, [wait2, wait3]).Validate)
            .Message.ShouldBe(refusal);

    [Fact]
    public void One_wait_stops_the_start_up() =>
        Should.Throw<InvalidOperationException>(new DeliverySettings(retryDelaysSeconds: [5]).Validate)
            .Message.ShouldBe("The Notifications:Delivery:RetryDelaysSeconds setting must hold exactly two waits, each from one to three hundred seconds.");
}
