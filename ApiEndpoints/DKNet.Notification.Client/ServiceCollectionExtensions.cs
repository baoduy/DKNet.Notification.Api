using Microsoft.Extensions.DependencyInjection;

namespace DKNet.Notification.Client;

/// <summary>Registers <see cref="INotificationClient" /> in a caller's service collection.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the client with the service address only: its requests carry no credential.</summary>
    public static IServiceCollection AddNotificationClient(this IServiceCollection services, Uri baseAddress) =>
        throw new NotImplementedException();

    /// <summary>Registers the client and runs the caller's own token handler on every request it sends.</summary>
    public static IServiceCollection AddNotificationClient(
        this IServiceCollection services,
        Uri baseAddress,
        Type messageHandlerType) =>
        throw new NotImplementedException();
}
