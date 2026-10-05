using System.Diagnostics.Metrics;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.AppServices.Templates;
using DKNet.Notification.Domains.Notifications;
using DKNet.Notification.Domains.Templates;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>DRK-2013 steps 4 and 5, and the "Logs and metrics" entries and counts of each outcome.</summary>
public sealed class SendNotificationServiceTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly TestLogCapture _logs = new();
    private readonly MeterListener _listener = new();
    private readonly List<(string Instrument, long Value, Dictionary<string, object?> Tags)> _measurements = [];
    private readonly SendNotificationService _service;

    public SendNotificationServiceTests()
    {
        _services = new ServiceCollection()
            .AddMetrics()
            .AddLogging(logging => logging.AddProvider(_logs))
            .AddSingleton<ITemplateCatalogue>(new OneTemplate("account-opened"))
            .AddSingleton(new EmailChannelSettings())
            .AddSingleton(new TeamsChannelSettings())
            .AddSingleton(new DeliverySettings())
            .AddSingleton<DeliveryQueue>()
            .AddSingleton<NotificationMetrics>()
            .AddSingleton<SendNotificationService>()
            .BuildServiceProvider();
        var meterFactory = _services.GetRequiredService<IMeterFactory>();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, meterFactory) && instrument.Meter.Name == "DKNet.Notification")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            _measurements.Add((instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        _listener.Start();
        _service = _services.GetRequiredService<SendNotificationService>();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _services.Dispose();
    }

    [Fact]
    public void A_missing_request_or_meter_factory_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => _service.Send(null!, "treasury-ops", "trace-0")).ParamName.ShouldBe("request");
        Should.Throw<ArgumentNullException>(() => new NotificationMetrics(null!)).ParamName.ShouldBe("meterFactory");
    }

    private static SendNotificationRequest Request(string templateId, string channel) =>
        new(channel, templateId, new Dictionary<string, string> { ["to"] = "jane@example.com" });

    [Fact]
    public void A_registered_template_is_skipped_logged_and_counted()
    {
        var notification = _service.Send(Request("account-opened", "WhatsApp"), "treasury-ops", "trace-1");

        notification.Status.ShouldBe(NotificationStatus.Skipped);
        notification.SkipReason.ShouldBe(SkipReason.ChannelNotSupported);
        var entry = _logs.Entries.ShouldHaveSingleItem();
        entry.Category.ShouldBe(typeof(SendNotificationService).FullName);
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.EventId.Id.ShouldBe(2001);
        entry.EventId.Name.ShouldBe("NotificationSkipped");
        entry.Value("NotificationId").ShouldBe(notification.NotificationId.ToString());
        entry.Value("Reason").ShouldBe("ChannelNotSupported");
        entry.Value("TemplateId").ShouldBe("account-opened");
        entry.Value("Channel").ShouldBe("whatsapp");
        entry.Value("CallerId").ShouldBe("treasury-ops");
        entry.Value("TraceId").ShouldBe("trace-1");
        var measurement = _measurements.ShouldHaveSingleItem();
        measurement.Instrument.ShouldBe("notifications.accepted");
        measurement.Value.ShouldBe(1);
        measurement.Tags.ShouldBe(new Dictionary<string, object?> { ["channel"] = "whatsapp", ["outcome"] = "skipped" });
    }

    [Theory]
    [InlineData("account-closed")]
    [InlineData("Account-Opened")]
    public void An_unknown_template_is_rejected_logged_and_counted(string templateId)
    {
        var notification = _service.Send(Request(templateId, "email"), "treasury-ops", "trace-2");

        notification.Status.ShouldBe(NotificationStatus.Rejected);
        var entry = _logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.EventId.Name.ShouldBe("NotificationRejected");
        entry.Value("NotificationId").ShouldBe(notification.NotificationId.ToString());
        entry.Value("Code").ShouldBe("TEMPLATE_NOT_FOUND");
        entry.Value("TemplateId").ShouldBe(templateId);
        entry.Value("Channel").ShouldBe("email");
        entry.Value("CallerId").ShouldBe("treasury-ops");
        entry.Value("TraceId").ShouldBe("trace-2");
        var measurement = _measurements.ShouldHaveSingleItem();
        measurement.Instrument.ShouldBe("notifications.rejected");
        measurement.Value.ShouldBe(1);
        measurement.Tags.ShouldBe(new Dictionary<string, object?> { ["code"] = "TEMPLATE_NOT_FOUND" });
    }

    [Fact]
    public void An_invalid_call_is_logged_with_a_new_id_and_counted()
    {
        _service.RejectInvalid("treasury-ops", "trace-3");

        var entry = _logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.EventId.Name.ShouldBe("NotificationRejected");
        Guid.Parse(entry.Value("NotificationId")!).Version.ShouldBe(7);
        entry.Value("Code").ShouldBe("INVALID_REQUEST");
        entry.Value("CallerId").ShouldBe("treasury-ops");
        entry.Value("TraceId").ShouldBe("trace-3");
        entry.State.Single(p => p.Key == "TemplateId").Value.ShouldBeNull();
        entry.State.Single(p => p.Key == "Channel").Value.ShouldBeNull();
        var measurement = _measurements.ShouldHaveSingleItem();
        measurement.Instrument.ShouldBe("notifications.rejected");
        measurement.Tags.ShouldBe(new Dictionary<string, object?> { ["code"] = "INVALID_REQUEST" });
    }

    [Fact]
    public void Caller_text_is_sanitized_before_it_is_logged()
    {
        _service.Send(Request("account-opened\r\nforged", "email\n"), "treasury-ops\r\n", "trace-4");

        var entry = _logs.Entries.ShouldHaveSingleItem();
        entry.Value("TemplateId").ShouldBe("account-openedforged");
        entry.Value("Channel").ShouldBe("email");
        entry.Value("CallerId").ShouldBe("treasury-ops");
    }

    [Fact]
    public void No_entry_or_tag_holds_a_parameter_value()
    {
        _service.Send(Request("account-opened", "email"), "treasury-ops", "trace-5");
        _service.Send(Request("account-closed", "email"), "treasury-ops", "trace-6");

        _logs.Entries.Count.ShouldBe(2);
        _logs.Entries.SelectMany(e => e.State.Select(p => Convert.ToString(p.Value)).Append(e.Message))
            .ShouldAllBe(text => text == null || !text.Contains("jane@example.com"));
        _measurements.SelectMany(m => m.Tags.Values).ShouldAllBe(tag => !Equals(tag, "jane@example.com"));
    }

    [Fact]
    public async Task The_handler_answers_an_accepted_notification_with_its_id()
    {
        var result = await new SendNotificationHandler(_service).OnHandle(
            new SendNotification(Request("account-opened", "WhatsApp"), "treasury-ops", "trace-7"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ToString().ShouldBe(_logs.Entries.ShouldHaveSingleItem().Value("NotificationId"));
    }

    [Fact]
    public async Task The_handler_fails_a_rejected_notification_with_its_code_and_field()
    {
        var result = await new SendNotificationHandler(_service).OnHandle(
            new SendNotification(Request("account-closed", "email"), "treasury-ops", "trace-8"),
            CancellationToken.None);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Metadata[SendNotification.CodeMetadata].ShouldBe("TEMPLATE_NOT_FOUND");
        error.Metadata[SendNotification.FieldMetadata].ShouldBe("templateId");
    }

    private sealed class OneTemplate(string templateId) : ITemplateCatalogue
    {
        private readonly NotificationTemplate _template = new(templateId, string.Empty, []);

        public IReadOnlyCollection<NotificationTemplate> Templates => [_template];

        public NotificationTemplate? Find(string id) => string.Equals(id, templateId, StringComparison.Ordinal) ? _template : null;
    }
}
