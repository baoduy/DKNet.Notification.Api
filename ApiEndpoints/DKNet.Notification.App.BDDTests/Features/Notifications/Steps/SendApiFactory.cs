using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.AppServices.Templates;
using DKNet.Notification.Domains.Templates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace DKNet.Notification.App.BDDTests.Features.Notifications.Steps;

/// <summary>
/// The host one send scenario runs against, booted fresh for each scenario so its logs, counters and catalogue gate
/// are its own. Settings go in through <c>UseSetting</c>, which <c>Program.cs</c> reads before its early
/// <c>FeatureOptions</c> bind (the same way the start-up scenarios turn sign-in on).
/// </summary>
/// <param name="signIn">Turns <c>FeatureManagement:RequireAuthorization</c> on, with <see cref="TestAuthHandler" />
/// standing in for Entra ID.</param>
/// <param name="redisConnection">The Redis the idempotency store uses; null keeps the in-memory store.</param>
/// <param name="environment">The host environment: <c>Testing</c>, or <c>Development</c> for a local run.</param>
/// <param name="settings">More settings, such as the email settings, set the same way before the host is built.</param>
/// <param name="graph">
/// Where the Graph sender signs in and sends: the scenario's Graph and token stubs. Null points it at a port where
/// nothing listens, so no test host ever reaches Microsoft.
/// </param>
public sealed class SendApiFactory(
    bool signIn,
    string? redisConnection,
    string environment = "Testing",
    IReadOnlyDictionary<string, string?>? settings = null,
    GraphEndpoints? graph = null)
    : TestApiFactoryBase
{
    // Port 9 (discard) on the loopback: a connection there is refused.
    private static readonly Uri Nowhere = new("https://127.0.0.1:9/");

    /// <summary>A settings source above every other one, changed while the service runs.</summary>
    public SettingsOverride Settings { get; } = new();

    /// <summary>The test decorator around the released catalogue; throws if the host resolves anything else.</summary>
    public GatedTemplateCatalogue Gate =>
        Services.GetRequiredService<ITemplateCatalogue>().ShouldBeOfType<GatedTemplateCatalogue>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment(environment);
        builder.UseSetting("FeatureManagement:RequireAuthorization", signIn ? "true" : "false");
        builder.UseSetting("ConnectionStrings:Redis", redisConnection ?? string.Empty);
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration((_, config) => config.Add(Settings));
        builder.ConfigureTestServices(services =>
        {
            if (signIn)
            {
                TestAuthHandler.Register(services);
            }

            // The test mail servers' authority, through the trust seam only: no setting can do this (DRK-2020 R6).
            services.AddSingleton(new SmtpTrustedRoots([TestCertificateAuthority.Trusted.Certificate]));

            // The webhook stubs' authority, through the Teams trust seam only: no setting can do this (DRK-2035 §3).
            services.AddSingleton(new TeamsTrustedRoots([TestCertificateAuthority.Trusted.Certificate]));

            // The Graph and token stubs, through the Graph endpoints seam only: no setting can do this (DRK-2028 §3).
            services.AddSingleton(graph ?? new GraphEndpoints(Nowhere, Nowhere, [], serviceAccountTokenFile: null));

            var released = services.Last(d => d.ServiceType == typeof(ITemplateCatalogue));
            services.Remove(released);
            services.AddSingleton<ITemplateCatalogue>(provider => new GatedTemplateCatalogue(
                (ITemplateCatalogue)(released.ImplementationInstance ?? released.ImplementationFactory!(provider))));
        });
    }
}

/// <summary>
/// Test-only decorator around the released catalogue. Inert until <see cref="Hold" />: then the next lookup
/// signals <see cref="Entered" /> and waits for <see cref="Release" />, so a call stays "still running" while the
/// scenario sends its repeat. It also holds the templates a scenario brings (<see cref="Add" />), found before the
/// released ones: the release ships no Teams version of any template (DRK-2035 §3 "Tests"). No production seam.
/// </summary>
public sealed class GatedTemplateCatalogue(ITemplateCatalogue released) : ITemplateCatalogue, IDisposable
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _open = new(initialState: true);
    private readonly ConcurrentDictionary<string, NotificationTemplate> _added = new(StringComparer.Ordinal);

    public IReadOnlyCollection<NotificationTemplate> Templates =>
        [.. _added.Values, .. released.Templates.Where(t => !_added.ContainsKey(t.TemplateId))];

    /// <summary>Adds a test template, or replaces the one with its id, while the service runs.</summary>
    public void Add(NotificationTemplate template) => _added[template.TemplateId] = template;

    /// <summary>Completes when a held lookup is waiting.</summary>
    public Task Entered => _entered.Task;

    public void Hold() => _open.Reset();

    public void Release() => _open.Set();

    public NotificationTemplate? Find(string templateId)
    {
        if (!_open.IsSet)
        {
            _entered.TrySetResult();
            _open.Wait(TimeSpan.FromSeconds(30));
        }

        return _added.TryGetValue(templateId, out var added) ? added : released.Find(templateId);
    }

    public void Dispose() => _open.Dispose();
}

/// <summary>An in-memory settings source whose changes raise the reload signal, as a settings refresh does.</summary>
public sealed class SettingsOverride : ConfigurationProvider, IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public void Change(string key, string? value)
    {
        Data[key] = value;
        OnReload();
    }
}

/// <summary>
/// Reads the service's counters with the BCL <see cref="MeterListener" />: meter <c>DKNet.Notification</c>, created
/// through the host's own <see cref="IMeterFactory" /> (so another host in the same process never leaks in).
/// </summary>
public sealed class NotificationMetricsCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<Measurement> _measurements = new();

    public NotificationMetricsCapture(IMeterFactory scope)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "DKNet.Notification" && ReferenceEquals(instrument.Meter.Scope, scope))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((i, value, tags, _) => Record(i, value, tags));
        _listener.SetMeasurementEventCallback<int>((i, value, tags, _) => Record(i, value, tags));
        _listener.SetMeasurementEventCallback<double>((i, value, tags, _) => Record(i, value, tags));
        _listener.Start();
    }

    public IReadOnlyCollection<Measurement> Measurements => _measurements.ToArray();

    /// <summary>The sum of <paramref name="instrument" /> over measurements carrying every tag given, exactly.</summary>
    public double Sum(string instrument, params (string Key, string Value)[] tags) =>
        Measurements
            .Where(m => m.Instrument == instrument)
            .Where(m => tags.All(t => m.Tags.TryGetValue(t.Key, out var value) && value == t.Value))
            .Sum(m => m.Value);

    /// <summary>
    /// The current value of a gauge: observable instruments are read now, then the last measurement is taken, so
    /// this works for an observable and for a recorded gauge alike. Null when the gauge never reported.
    /// </summary>
    public double? Current(string instrument)
    {
        _listener.RecordObservableInstruments();
        return Measurements.LastOrDefault(m => m.Instrument == instrument)?.Value;
    }

    public void Dispose() => _listener.Dispose();

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var tagValues = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            tagValues[tag.Key] = Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        _measurements.Enqueue(new Measurement(instrument.Name, value, tagValues));
    }

    public sealed record Measurement(string Instrument, double Value, IReadOnlyDictionary<string, string?> Tags);
}
