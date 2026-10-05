using DKNet.Notification.AppServices.Delivery;
using SlimMessageBus.Host;
using SlimMessageBus.Host.Memory;

namespace DKNet.Notification.Api.Configs;

[ExcludeFromCodeCoverage]
internal static class ServiceConfigs
{
    #region Methods

    public static IServiceCollection AddAllAppServices(this IServiceCollection services) =>
        services
            // The one clock: delivery waits and acceptance times read it, so a test can swap in a fake one.
            .AddSingleton(TimeProvider.System)
            .AddSingleton<NotificationMetrics>()
            .AddSingleton<SendNotificationService>()
            // Stopgap until the status wiring: AddServicesFromAssembly now finds DeliveryConsumer, which needs the store.
            .AddDistributedMemoryCache()
            .AddSingleton(new NotificationStatusSettings())
            .AddSingleton<NotificationStatusStore>()
            // Task 5 replaces this: the real backlog counts the Redis list.
            .AddSingleton<IDeliveryBacklog, InProcessDeliveryBacklog>()
            // In-memory bus as the mediator from endpoint to handler (ADR-0011); not the delivery queue (ADR-0003).
            .AddSlimMessageBus(mbb => mbb
                .WithProviderMemory(cf =>
                {
                    cf.EnableMessageHeaders = false;
                    cf.EnableMessageSerialization = false;
                })
                .AutoDeclareFrom(typeof(SendNotification).Assembly)
                .AddServicesFromAssembly(typeof(SendNotification).Assembly));

    public static IServiceCollection AddOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure core options for the application
        services.Configure<FeatureOptions>(configuration.GetSection(FeatureOptions.Name));

        services.ConfigureHttpJsonOptions(op =>
        {
            op.SerializerOptions.PropertyNamingPolicy = SharedConsts.JsonSerializerOptions.PropertyNamingPolicy;
            op.SerializerOptions.DefaultIgnoreCondition = SharedConsts.JsonSerializerOptions.DefaultIgnoreCondition;
            op.SerializerOptions.WriteIndented = SharedConsts.JsonSerializerOptions.WriteIndented;
            op.SerializerOptions.PropertyNameCaseInsensitive =
                SharedConsts.JsonSerializerOptions.PropertyNameCaseInsensitive;
            op.SerializerOptions.DictionaryKeyPolicy = SharedConsts.JsonSerializerOptions.DictionaryKeyPolicy;

            op.SerializerOptions.Converters.Clear();
            foreach (var converter in SharedConsts.JsonSerializerOptions.Converters)
            {
                op.SerializerOptions.Converters.Add(converter);
            }
        });

        return services;
    }

    #endregion
}