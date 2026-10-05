using System.Net;
using System.Text;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.AppServices.Delivery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace DKNet.Notification.App.Tests.Integration.Notifications;

/// <summary>
/// Spec §4 "Child buses", through the real host without Redis: an accepted call answers before its delivery ends,
/// because <c>DeliverNotification</c> goes to the Delivery bus (memory, non-blocking publish) and not to the mediator,
/// whose publish would run the consumer inside the call.
/// </summary>
public sealed class DeliveryRoutingTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task An_accepted_call_answers_before_its_delivery_ends()
    {
        var sender = new HeldSender();
        await using var factory = new EmailApiFactory(sender);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/notifications")
        {
            Content = new StringContent(
                """{"channel":"email","templateId":"account-opened","parameters":{"to":"jane@example.com","customerName":"Jane","accountNumber":"12345"}}""",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("Idempotency-Key", "routing-1");

        try
        {
            // The sender holds every delivery, so an inline delivery would never let the call answer.
            using var response = await client.SendAsync(request).WaitAsync(Patience);
            response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

            await sender.Called.WaitAsync(Patience);
        }
        finally
        {
            sender.Release();
        }
    }

    /// <summary>An email sender that signals each call and holds it until released.</summary>
    private sealed class HeldSender : IDeliverySender
    {
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Called => _called.Task;

        public void Release() => _released.TrySetResult();

        public async Task<DeliveryFailure?> SendAsync(
            Domains.Notifications.Notification notification,
            CancellationToken stoppingToken)
        {
            _called.TrySetResult();
            await _released.Task.WaitAsync(stoppingToken);
            return null;
        }
    }

    private sealed class EmailApiFactory(HeldSender sender) : TestApiFactoryBase
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ConnectionStrings:Redis", string.Empty);
            builder.UseSetting("Notifications:Email:Enabled", "true");
            builder.UseSetting("Notifications:Email:Sender", "Smtp");
            builder.UseSetting("Notifications:Email:Smtp:Host", "smtp.example.com");
            builder.UseSetting("Notifications:Email:Smtp:Port", "587");
            builder.UseSetting("Notifications:Email:Smtp:Security", "StartTls");
            builder.UseSetting("Notifications:Email:Smtp:FromAddress", "notifications@example.com");
            builder.ConfigureTestServices(services => services.AddSingleton<IDeliverySender>(sender));
        }
    }
}
