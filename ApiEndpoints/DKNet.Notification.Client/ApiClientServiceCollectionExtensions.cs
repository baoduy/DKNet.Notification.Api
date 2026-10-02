using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace DKNet.Notification.Client;

/// <summary>Registers the typed API client in a downstream system's service collection.</summary>
public static class ApiClientServiceCollectionExtensions
{
    /// <summary>Name of the <see cref="HttpClient" /> both typed clients share.</summary>
    public const string HttpClientName = "DKNet.Notification.Client";

    /// <summary>
    /// Registers <see cref="IProductsClient" /> and <see cref="IPurchaseOrdersClient" /> against
    /// <paramref name="baseAddress" />. <paramref name="handlerFactory" />, when given, supplies the caller's
    /// own <see cref="DelegatingHandler" /> (for example one that attaches the caller's bearer token); the
    /// client attaches no credential itself.
    /// </summary>
    /// <returns>The <see cref="IHttpClientBuilder" /> shared by both clients, for further configuration.</returns>
    public static IHttpClientBuilder AddApiClient(
        this IServiceCollection services,
        Uri baseAddress,
        Func<DelegatingHandler>? handlerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);

        var builder = services.AddHttpClient(HttpClientName, c => c.BaseAddress = baseAddress)
            .AddHttpMessageHandler(() => new IdempotencyKeyGuard());
        if (handlerFactory is not null) builder.AddHttpMessageHandler(handlerFactory);

        services.AddTransient(sp => RestService.For<IProductsClient>(CreateHttpClient(sp)));
        services.AddTransient(sp => RestService.For<IPurchaseOrdersClient>(CreateHttpClient(sp)));
        return builder;
    }

    private static HttpClient CreateHttpClient(IServiceProvider services) =>
        services.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
}
