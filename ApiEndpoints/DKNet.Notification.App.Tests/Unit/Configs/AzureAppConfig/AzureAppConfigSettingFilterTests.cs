using DKNet.Notification.Api.Configs.AzureAppConfig;
using Microsoft.Extensions.Configuration;

namespace DKNet.Notification.App.Tests.Unit.Configs.AzureAppConfig;

/// <summary>
/// DRK-2337 reproduction (R3–R5, finding B1 on DRK-2340): the label selector <c>op.Select(KeyFilter.Any, label)</c>
/// also fetches feature flags that carry the service label. <see cref="AzureAppConfigOptions.ShouldLoadSetting" />
/// decides which fetched settings are kept: <c>LoadFeatureFlags=false</c> keeps no flag, a <c>FeatureFlagPrefix</c>
/// keeps only matching flags, and plain keys are always kept. Every expected value is a literal from the brief
/// (DRK-2357 §6a D13–D16).
/// </summary>
public sealed class AzureAppConfigSettingFilterTests
{
    #region Methods

    private static AzureAppConfigOptions Bind(string? loadFeatureFlags, string? featureFlagPrefix) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["AzureAppConfiguration:LoadFeatureFlags"] = loadFeatureFlags,
                ["AzureAppConfiguration:FeatureFlagPrefix"] = featureFlagPrefix
            })
            .Build()
            .GetSection(AzureAppConfigOptions.Name)
            .Get<AzureAppConfigOptions>() ?? new AzureAppConfigOptions();

    // D14–D16
    [Theory]
    [InlineData("false", "", ".appconfig.featureflag/X", false)]
    [InlineData("false", "Notification.", ".appconfig.featureflag/Notification.A", false)]
    [InlineData("true", "Notification.", ".appconfig.featureflag/Notification.A", true)]
    [InlineData("true", "Notification.", ".appconfig.featureflag/Other.B", false)]
    [InlineData("true", "Notification.", ".appconfig.featureflag/notification.A", false)]
    [InlineData("true", "", ".appconfig.featureflag/X", true)]
    [InlineData("true", "   ", ".appconfig.featureflag/X", true)]
    [InlineData(null, null, ".appconfig.featureflag/X", true)]
    public void FeatureFlag_IsKept_OnlyWhenFlagsAreOn_AndItsNameStartsWithThePrefix(
        string? loadFeatureFlags, string? featureFlagPrefix, string key, bool expected)
    {
        var options = Bind(loadFeatureFlags, featureFlagPrefix);

        options.ShouldLoadSetting(key).ShouldBe(expected);
    }

    // D13, R5
    [Theory]
    [InlineData("false", "Notification.")]
    [InlineData("false", "")]
    [InlineData("true", "Notification.")]
    [InlineData("true", "")]
    [InlineData(null, null)]
    public void PlainKey_IsAlwaysKept_WhateverTheFlagSettings(string? loadFeatureFlags, string? featureFlagPrefix)
    {
        var options = Bind(loadFeatureFlags, featureFlagPrefix);

        options.ShouldLoadSetting("Smtp:Host").ShouldBeTrue();
        options.ShouldLoadSetting("Settings/.appconfig.featureflag/X").ShouldBeTrue();
    }

    #endregion
}
