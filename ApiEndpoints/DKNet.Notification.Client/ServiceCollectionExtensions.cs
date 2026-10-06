using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace DKNet.Notification.Client;

/// <summary>Registers <see cref="INotificationClient" /> in a caller's service collection.</summary>
/// <remarks>
/// The client adds no credential, no retry and no resilience handler: one call is one request (R3, R4). Every
/// non-success answer, and a success body it cannot read, reaches the caller as a
/// <see cref="NotificationApiException" />; a transport failure reaches it unchanged.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the client with the service address only: its requests carry no credential.</summary>
    public static IServiceCollection AddNotificationClient(this IServiceCollection services, Uri baseAddress)
    {
        AddClient(services, baseAddress);
        return services;
    }

    /// <summary>Registers the client and runs the caller's own token handler on every request it sends.</summary>
    /// <param name="services">The caller's service collection.</param>
    /// <param name="baseAddress">The notification service's address.</param>
    /// <param name="messageHandlerType">
    /// A <see cref="DelegatingHandler" /> registered in <paramref name="services" />, resolved for each handler chain.
    /// </param>
    public static IServiceCollection AddNotificationClient(
        this IServiceCollection services,
        Uri baseAddress,
        Type messageHandlerType)
    {
        ArgumentNullException.ThrowIfNull(messageHandlerType);
        if (!typeof(DelegatingHandler).IsAssignableFrom(messageHandlerType))
        {
            throw new ArgumentException(
                $"{messageHandlerType} is not a {nameof(DelegatingHandler)}.",
                nameof(messageHandlerType));
        }

        AddClient(services, baseAddress)
            .AddHttpMessageHandler(sp => (DelegatingHandler)sp.GetRequiredService(messageHandlerType));
        return services;
    }

    private static IHttpClientBuilder AddClient(IServiceCollection services, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        return services.AddRefitGeneratedClient<INotificationClient>(Settings())
            .ConfigureHttpClient(client => client.BaseAddress = baseAddress)
            // No header value reaches a log, the caller's Authorization included (R4).
            .RedactLoggedHeaders(_ => true);
    }

    private static RefitSettings Settings() =>
        new(new SystemTextJsonContentSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            // A 200 body without one of the contract's fields is not a success.
            RespectRequiredConstructorParameters = true
        }))
        {
            ExceptionFactory = async response => response.IsSuccessStatusCode
                ? null
                : await NotificationApiException.FromResponseAsync(response).ConfigureAwait(false),
            DeserializationExceptionFactory = (response, exception) => ValueTask.FromResult<Exception?>(
                new NotificationApiException(
                    response.StatusCode,
                    [],
                    $"The notification service answered {(int)response.StatusCode} ({response.StatusCode}) with a body the client cannot read: {exception.Message}")),
            TransportExceptionFactory = (_, exception, _) => exception
        };
}
