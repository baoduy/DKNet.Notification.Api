using System.Text;
using DKNet.Notification.Api.Configs.AzureAppConfig;
using Microsoft.Extensions.Configuration;

namespace DKNet.Notification.App.Tests.Unit.Configs.AzureAppConfig;

/// <summary>
/// DRK-2337 reproduction: the Azure App Configuration options are read from the documented
/// <c>AzureAppConfiguration</c> section, and the effective label, feature-flag filter, refresh interval and
/// connection-string name follow the owner's rules (R1, R2, R4, R6). Binds the options exactly the way
/// <see cref="AzureAppConfigSetup.AddAzureAppConfig" /> does — no host, no store. Every expected value is a literal
/// from the brief (DRK-2357 §6a).
/// </summary>
public sealed class AzureAppConfigOptionsTests
{
    #region Methods

    private static AzureAppConfigOptions Bind(IConfiguration config) =>
        config.GetSection(AzureAppConfigOptions.Name).Get<AzureAppConfigOptions>() ?? new AzureAppConfigOptions();

    private static AzureAppConfigOptions Bind(params (string Key, string? Value)[] settings) =>
        Bind(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal))
            .Build());

    private static AzureAppConfigOptions BindJson(string json) =>
        Bind(new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build());

    private static IConfigurationRoot LoadShippedAppSettings() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

    // D8
    [Fact]
    public void OptionsSetUnderAzureAppConfiguration_AreRead()
    {
        var options = Bind(
            ("AzureAppConfiguration:Label", "prod"),
            ("AzureAppConfiguration:LoadFeatureFlags", "false"),
            ("AzureAppConfiguration:FeatureFlagPrefix", "Notification."),
            ("AzureAppConfiguration:CacheExpirationInSeconds", "60"));

        options.Label.ShouldBe("prod");
        options.LoadFeatureFlags.ShouldBeFalse();
        options.FeatureFlagPrefix.ShouldBe("Notification.");
        options.ResolveRefreshInterval().ShouldBe(TimeSpan.FromSeconds(60));
    }

    // D8
    [Fact]
    public void OptionsSetOnlyUnderTheLegacyAzureAppConfigSection_AreNotRead()
    {
        var options = Bind(
            ("AzureAppConfig:Label", "prod"),
            ("AzureAppConfig:LoadFeatureFlags", "false"),
            ("AzureAppConfig:FeatureFlagPrefix", "Notification."),
            ("AzureAppConfig:CacheExpirationInSeconds", "60"));

        options.Label.ShouldBeNull();
        options.LoadFeatureFlags.ShouldBeTrue();
        options.ResolveFeatureFlagFilter().ShouldBeNull();
        options.ResolveRefreshInterval().ShouldBe(TimeSpan.FromSeconds(300));
    }

    // D1–D4
    [Theory]
    [InlineData(null, "DKNet.Notification.Api")]
    [InlineData("", "DKNet.Notification.Api")]
    [InlineData("   ", "DKNet.Notification.Api")]
    [InlineData("prod", "prod")]
    public void Label_ResolvesToTheServiceName_WhenBlank_ElseAsSet(string? label, string expected)
    {
        var options = Bind(("AzureAppConfiguration:Label", label));

        options.ResolveLabel().ShouldBe(expected);
    }

    // D5
    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData(null, true)]
    public void LoadFeatureFlags_IsOff_OnlyWhenSetToFalse(string? value, bool expected)
    {
        var options = Bind(("AzureAppConfiguration:LoadFeatureFlags", value));

        options.LoadFeatureFlags.ShouldBe(expected);
    }

    // D5
    [Fact]
    public void LoadFeatureFlags_IsOn_WhenAbsent()
    {
        var options = Bind(("AzureAppConfiguration:Label", "prod"));

        options.LoadFeatureFlags.ShouldBeTrue();
    }

    // D5
    [Fact]
    public void LoadFeatureFlags_IsOn_WhenJsonNull()
    {
        var options = BindJson("""{ "AzureAppConfiguration": { "Label": "prod", "LoadFeatureFlags": null } }""");

        options.LoadFeatureFlags.ShouldBeTrue();
    }

    // D6, D7
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("Notification.", "Notification.*")]
    public void FeatureFlagPrefix_FiltersFlagsByPrefix_OnlyWhenNotBlank(string? prefix, string? expected)
    {
        var options = Bind(("AzureAppConfiguration:FeatureFlagPrefix", prefix));

        options.ResolveFeatureFlagFilter().ShouldBe(expected);
    }

    // D6
    [Fact]
    public void FeatureFlagPrefix_Absent_LoadsAllFlags()
    {
        var options = Bind(("AzureAppConfiguration:Label", "prod"));

        options.ResolveFeatureFlagFilter().ShouldBeNull();
    }

    // D9–D11
    [Theory]
    [InlineData(null, 300)]
    [InlineData("60", 60)]
    [InlineData("1", 1)]
    [InlineData("0", 300)]
    [InlineData("-5", 300)]
    public void RefreshInterval_IsCacheExpirationInSeconds_FallingBackTo300Seconds(string? value, int expectedSeconds)
    {
        var options = Bind(("AzureAppConfiguration:CacheExpirationInSeconds", value));

        options.ResolveRefreshInterval().ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    // D9
    [Fact]
    public void RefreshInterval_Absent_Is300Seconds()
    {
        var options = Bind(("AzureAppConfiguration:Label", "prod"));

        options.ResolveRefreshInterval().ShouldBe(TimeSpan.FromSeconds(300));
    }

    // D12
    [Theory]
    [InlineData(null, "AzureAppConfig")]
    [InlineData("Custom", "Custom")]
    public void ConnectionStringName_DefaultsToAzureAppConfig_ElseAsSet(string? name, string expected)
    {
        var options = Bind(("AzureAppConfiguration:ConnectionStringName", name));

        options.ConnectionStringName.ShouldBe(expected);
    }

    // D12
    [Fact]
    public void ConnectionStringName_NotSet_IsAzureAppConfig()
    {
        var options = Bind(("AzureAppConfiguration:Label", "prod"));

        options.ConnectionStringName.ShouldBe("AzureAppConfig");
    }

    // D12
    [Fact]
    public void ConnectionStringName_JsonNull_IsAzureAppConfig()
    {
        var options = BindJson("""{ "AzureAppConfiguration": { "Label": "prod", "ConnectionStringName": null } }""");

        options.ConnectionStringName.ShouldBe("AzureAppConfig");
    }

    // §3 row 7
    [Fact]
    public void ShippedAppSettings_CarriesTheSection_WithoutKeyPrefix()
    {
        var config = LoadShippedAppSettings();

        config.GetSection(AzureAppConfigOptions.Name).Exists().ShouldBeTrue();
        config["AzureAppConfiguration:KeyPrefix"].ShouldBeNull();
        Bind(config).ConnectionStringName.ShouldBe("AzureAppConfig");
    }

    // §3 row 7, D2
    [Fact]
    public void ShippedAppSettings_ResolvesToTheServiceLabel_AllFlags_And300SecondRefresh()
    {
        var options = Bind(LoadShippedAppSettings());

        options.Label.ShouldBe(string.Empty);
        options.ResolveLabel().ShouldBe("DKNet.Notification.Api");
        options.LoadFeatureFlags.ShouldBeTrue();
        options.ResolveFeatureFlagFilter().ShouldBeNull();
        options.ResolveRefreshInterval().ShouldBe(TimeSpan.FromSeconds(300));
    }

    #endregion
}
