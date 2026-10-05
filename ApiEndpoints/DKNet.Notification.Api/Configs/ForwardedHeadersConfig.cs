using Microsoft.AspNetCore.HttpOverrides;

namespace DKNet.Notification.Api.Configs;

[ExcludeFromCodeCoverage]
internal static class ForwardedHeadersConfig
{
    #region Methods

    public static IServiceCollection AddForwardedHeadersConfig(
        this IServiceCollection services,
        FeatureOptions features,
        IConfiguration configuration)
    {
        if (!features.EnableForwardedHeaders)
        {
            return services;
        }

        var trustedProxies = (configuration.GetSection("Security:TrustedProxies").Get<string[]>() ?? [])
            .Select(IPAddress.Parse)
            .ToArray();

        // Fully qualified: Microsoft.AspNetCore.HttpOverrides (imported above) also exports an IPNetwork,
        // so the bare name is ambiguous (CS0104) — ForwardedHeadersOptions.KnownIPNetworks needs System.Net's.
        var trustedNetworks = (configuration.GetSection("Security:TrustedNetworks").Get<string[]>() ?? [])
            .Select(System.Net.IPNetwork.Parse)
            .ToArray();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            // ASP.NET Core seeds KnownProxies/KnownIPNetworks with loopback — clear first so "no trusted proxy
            // configured" is actually empty, not an unmodified default.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            if (trustedProxies.Length == 0 && trustedNetworks.Length == 0)
            {
                // With no trusted proxy, ForwardedHeadersMiddleware's own restriction check is a no-op when both
                // lists are empty (it trusts the header from ANY peer, not none) — so forwarded headers must be
                // disabled outright here to actually ignore them (R1).
                options.ForwardedHeaders = ForwardedHeaders.None;
                return;
            }

            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var proxy in trustedProxies)
            {
                options.KnownProxies.Add(proxy);
            }

            foreach (var network in trustedNetworks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });

        services.MarkConfigAdded(nameof(ForwardedHeadersConfig));
        return services;
    }

    public static WebApplication UseForwardedHeadersConfig(this WebApplication app)
    {
        if (app.Services.IsConfigAdded(nameof(ForwardedHeadersConfig)))
        {
            app.UseForwardedHeaders();
            app.Logger.LogInformation("{Feature} enabled", nameof(ForwardedHeadersConfig));
        }

        return app;
    }

    #endregion
}
