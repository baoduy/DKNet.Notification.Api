using System.Diagnostics.Metrics;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.App.Tests.Unit.Delivery;
using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.AppServices.Templates;
using DKNet.Notification.Domains.Templates;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using SlimMessageBus;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>
///     The send service of one test, composed from its settings the way the API composes it, with its log entries and
///     counter measurements captured.
/// </summary>
internal sealed class SendServiceHost : IDisposable
{
    private readonly MeterListener _listener = new();
    private ServiceProvider? _services;

    public TestLogCapture Logs { get; } = new();

    /// <summary>The delivery bus: records what the service publishes.</summary>
    public RecordingBus Bus { get; } = new();

    /// <summary>The queue length the service reads; a test sets it to fill the queue.</summary>
    public FixedBacklog Backlog { get; } = new();

    /// <summary>The caller-scoped status of the notifications, in memory.</summary>
    public NotificationStatusStore Status => Services.GetRequiredService<NotificationStatusStore>();

    public List<(string Instrument, long Value, Dictionary<string, object?> Tags)> Measurements { get; } = [];

    public ServiceProvider Services => _services.ShouldNotBeNull();

    public void Dispose()
    {
        _listener.Dispose();
        _services?.Dispose();
    }

    public SendNotificationService Service(
        ITemplateCatalogue catalogue,
        EmailChannelSettings email,
        TeamsChannelSettings teams,
        DeliverySettings? delivery = null)
    {
        _services = new ServiceCollection()
            .AddMetrics()
            .AddLogging(logging => logging.AddProvider(Logs))
            .AddSingleton(catalogue)
            .AddSingleton(email)
            .AddSingleton(teams)
            .AddSingleton(delivery ?? new DeliverySettings(queueCapacity: 10))
            .AddSingleton<IDeliveryBacklog>(Backlog)
            .AddSingleton<IMessageBus>(Bus)
            .AddDistributedMemoryCache()
            .AddSingleton(new NotificationStatusSettings())
            .AddSingleton<NotificationStatusStore>()
            .AddSingleton(TimeProvider.System)
            .AddSingleton<NotificationMetrics>()
            .AddSingleton<SendNotificationService>()
            .BuildServiceProvider();
        var meterFactory = _services.GetRequiredService<IMeterFactory>();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, meterFactory) && instrument is Counter<long>)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            Measurements.Add((instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        _listener.Start();
        return _services.GetRequiredService<SendNotificationService>();
    }

    /// <summary>A backlog whose length a test sets.</summary>
    public sealed class FixedBacklog : IDeliveryBacklog
    {
        public long Length { get; set; }

        public ValueTask<long> LengthAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Length);
    }

    /// <summary>A catalogue holding exactly <paramref name="templates" />, found by id with case.</summary>
    public sealed class FixedCatalogue(params NotificationTemplate[] templates) : ITemplateCatalogue
    {
        public IReadOnlyCollection<NotificationTemplate> Templates => templates;

        public NotificationTemplate? Find(string templateId) =>
            templates.FirstOrDefault(t => string.Equals(t.TemplateId, templateId, StringComparison.Ordinal));
    }
}
