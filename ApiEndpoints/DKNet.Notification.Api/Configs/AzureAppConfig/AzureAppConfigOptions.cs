using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;

namespace DKNet.Notification.Api.Configs.AzureAppConfig;

/// <summary>
///     Configuration options for Azure App Configuration integration
/// </summary>
internal sealed class AzureAppConfigOptions
{
    #region Fields

    /// <summary>
    ///     Refresh interval used when <see cref="CacheExpirationInSeconds" /> is absent or below one second
    /// </summary>
    private const int DefaultCacheExpirationInSeconds = 300;

    /// <summary>
    ///     Smallest refresh interval the SDK's <c>SetRefreshInterval</c> accepts, in seconds
    /// </summary>
    private const int MinCacheExpirationInSeconds = 1;

    /// <summary>
    ///     Connection string looked up when <see cref="ConnectionStringName" /> is unset or null
    /// </summary>
    private const string DefaultConnectionStringName = "AzureAppConfig";

    private string? _connectionStringName;

    #endregion

    #region Properties

    /// <summary>
    ///     Whether to load feature flags from Azure App Configuration. Unset or null loads them.
    /// </summary>
    public bool LoadFeatureFlags => LoadFeatureFlagsSetting ?? true;

    /// <summary>
    ///     The raw <c>LoadFeatureFlags</c> value as bound. Nullable so a key bound to null (.NET 10 binds null
    ///     values) keeps the default instead of turning flags off.
    /// </summary>
    [ConfigurationKeyName(nameof(LoadFeatureFlags))]
    public bool? LoadFeatureFlagsSetting { get; set; }

    /// <summary>
    ///     Refresh interval for configuration values, in seconds. Below one second falls back to 300.
    /// </summary>
    public int CacheExpirationInSeconds { get; set; } = DefaultCacheExpirationInSeconds;

    /// <summary>
    ///     Name of the <c>ConnectionStrings</c> entry holding the Azure App Configuration endpoint.
    ///     Unset or null looks up <c>AzureAppConfig</c>.
    /// </summary>
    [AllowNull]
    public string ConnectionStringName
    {
        get => _connectionStringName ?? DefaultConnectionStringName;
        set => _connectionStringName = value;
    }

    /// <summary>
    ///     Feature flag prefix to filter feature flags (optional). Blank loads all flags.
    /// </summary>
    public string? FeatureFlagPrefix { get; set; } = string.Empty;

    /// <summary>
    ///     Configuration section name
    /// </summary>
    public static string Name => "AzureAppConfiguration";

    /// <summary>
    ///     Label to filter configuration values (optional). Blank selects <see cref="SharedConsts.ApiName" />.
    /// </summary>
    public string? Label { get; set; }

    #endregion

    #region Methods

    /// <summary>
    ///     The label to select keys by: <see cref="Label" />, or <see cref="SharedConsts.ApiName" /> when blank
    /// </summary>
    public string ResolveLabel() => string.IsNullOrWhiteSpace(Label) ? SharedConsts.ApiName : Label;

    /// <summary>
    ///     The feature-flag key filter <c>&lt;prefix&gt;*</c>, or <see langword="null" /> to load all flags
    /// </summary>
    public string? ResolveFeatureFlagFilter() =>
        string.IsNullOrWhiteSpace(FeatureFlagPrefix) ? null : $"{FeatureFlagPrefix}*";

    /// <summary>
    ///     The configuration refresh interval: <see cref="CacheExpirationInSeconds" />, or 300 seconds when below one
    /// </summary>
    public TimeSpan ResolveRefreshInterval() =>
        TimeSpan.FromSeconds(CacheExpirationInSeconds < MinCacheExpirationInSeconds
            ? DefaultCacheExpirationInSeconds
            : CacheExpirationInSeconds);

    /// <summary>
    ///     Whether a setting fetched from the store is kept. Non-flag keys are always kept. A feature flag
    ///     (<c>.appconfig.featureflag/&lt;name&gt;</c>) is kept only when <see cref="LoadFeatureFlags" /> is on and its
    ///     name starts with <see cref="FeatureFlagPrefix" />, when one is set. This also covers flags the label
    ///     selector fetches.
    /// </summary>
    /// <param name="key">The key of the fetched setting</param>
    public bool ShouldLoadSetting(string key)
    {
        var flagPrefix = FeatureFlagConfigurationSetting.KeyPrefix;
        if (!key.StartsWith(flagPrefix, StringComparison.Ordinal))
        {
            return true;
        }

        return LoadFeatureFlags && (string.IsNullOrWhiteSpace(FeatureFlagPrefix) ||
                                    key.AsSpan(flagPrefix.Length).StartsWith(FeatureFlagPrefix, StringComparison.Ordinal));
    }

    #endregion
}
