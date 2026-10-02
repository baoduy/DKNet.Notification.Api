namespace DKNet.Notification.Api.Configs;

/// <summary>
///     Records that a given <c>Add*Config</c> was applied to THIS host, using a keyed singleton scoped to the
///     host's own <see cref="IServiceCollection" />/<see cref="IServiceProvider" />. Replaces the old static
///     "already configured" flags, which were shared across every host in the process — two hosts built in the
///     same process (e.g. two <c>WebApplicationFactory</c> instances) would otherwise leak each other's feature
///     state.
/// </summary>
internal static class HostConfigMarker
{
    public static IServiceCollection MarkConfigAdded(this IServiceCollection services, string configName)
    {
        services.AddKeyedSingleton(configName, new object());
        return services;
    }

    public static bool IsConfigAdded(this IServiceProvider services, string configName) =>
        services.GetKeyedService<object>(configName) is not null;
}
