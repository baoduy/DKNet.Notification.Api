using System.Text.RegularExpressions;
using DKNet.Notification.App.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.App.Tests.Integration.Notifications;

/// <summary>
/// DRK-2020 §3 "Email settings and start-up", for a value the settings binder cannot convert (brief DRK-2023 §3
/// row 14): an email or SMTP setting leaves email not configured and names the key; a delivery setting refuses the
/// start and names the key. Neither holds the value, though the binder's own message does. Through the real host
/// start, with the settings in through <c>UseSetting</c>, which the host reads before <c>Build()</c>.
/// </summary>
public sealed class UnconvertibleSettingStartupTests
{
    private const string Unconvertible = "abc";

    // A whole value, so "abc" is not found inside a longer word.
    private static readonly Regex UnconvertibleValue = new($"(?<![A-Za-z0-9]){Unconvertible}(?![A-Za-z0-9])");

    [Fact]
    public void An_email_setting_the_binder_cannot_convert_leaves_email_not_configured_and_names_the_key()
    {
        using var factory = new StartupApiFactory(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Notifications:Email:Enabled"] = "true",
            ["Notifications:Email:Sender"] = "Smtp",
            ["Notifications:Email:Smtp:Host"] = "smtp.example.com",
            ["Notifications:Email:Smtp:Port"] = Unconvertible,
            ["Notifications:Email:Smtp:Security"] = "StartTls",
            ["Notifications:Email:Smtp:FromAddress"] = "notifications@drunkcoding.net"
        });

        Should.NotThrow(() => factory.Services);

        var entries = factory.LogCapture.Entries;
        var warning = entries.Where(e => e.EventId.Name == "EmailSenderNotConfigured").ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Value("Settings").ShouldBe("Notifications:Email:Smtp:Port");
        entries.Where(e => e.EventId.Name == "EmailSenderStarted").ShouldBeEmpty();

        var logged = entries
            .SelectMany(e => e.State.Select(p => Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture))
                .Append(e.Message)
                .Append(e.Exception?.ToString()))
            .OfType<string>()
            .ToArray();
        logged.ShouldNotBeEmpty();
        logged.ShouldAllBe(text => !UnconvertibleValue.IsMatch(text), "a log entry holds the setting's value");
    }

    [Theory]
    [InlineData("Notifications:Delivery:QueueCapacity")]
    [InlineData("Notifications:Status:RetentionHours")]
    public void A_delivery_or_status_setting_the_binder_cannot_convert_stops_the_start_up_and_names_the_key(string key)
    {
        using var factory = new StartupApiFactory(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [key] = Unconvertible
        });

        var error = Should.Throw<Exception>(() => factory.Services);

        var messages = Chain(error).Select(e => e.Message).ToArray();
        messages.ShouldContain(
            message => message.Contains(key, StringComparison.Ordinal),
            $"the start-up must name {key}, but failed with: {error}");
        messages.ShouldAllBe(message => !UnconvertibleValue.IsMatch(message), "the refusal holds the setting's value");
    }

    private static IEnumerable<Exception> Chain(Exception error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private sealed class StartupApiFactory(IReadOnlyDictionary<string, string> settings) : TestApiFactoryBase
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }
}
