using DKNet.Notification.AppServices.Delivery;
using SlimMessageBus;
using SlimMessageBus.Host;
using SlimMessageBus.Host.Memory;
using SlimMessageBus.Host.Redis;
using SlimMessageBus.Host.Serialization.SystemTextJson;
using StackExchange.Redis;

namespace DKNet.Notification.Api.Configs;

[ExcludeFromCodeCoverage]
internal static class ServiceConfigs
{
    #region Methods

    /// <summary>
    ///     Adds the services and the bus: a mediator child bus for the endpoint requests (ADR-0011) and a delivery child
    ///     bus for <see cref="DeliverNotification" /> (ADR-0013), on Redis when <paramref name="redisConnectionString" />
    ///     is set and in memory otherwise (Development and Testing only, which <c>AppConfig</c> enforces).
    /// </summary>
    public static IServiceCollection AddAllAppServices(this IServiceCollection services, string? redisConnectionString)
    {
        services
            // The one clock: delivery waits and acceptance times read it, so a test can swap in a fake one.
            .AddSingleton(TimeProvider.System)
            .AddSingleton<NotificationMetrics>()
            .AddSingleton<SendNotificationService>();

        Lazy<IConnectionMultiplexer>? redis = null;
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IDeliveryBacklog, InProcessDeliveryBacklog>();
        }
        else
        {
            // One connection for the delivery bus and the backlog count, opened on first use, not at registration.
            // Not registered as IConnectionMultiplexer: the idempotency store registers its own, and the last one wins.
            redis = new Lazy<IConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(redisConnectionString));
            services.AddSingleton<IDeliveryBacklog>(_ => new RedisDeliveryBacklog(redis.Value));
        }

        var assembly = typeof(SendNotification).Assembly;
        return services.AddSlimMessageBus(mbb => mbb
            // The mediator from endpoint to handler (ADR-0011): every request except delivery.
            .AddChildBus("Mediator", child => child
                .WithProviderMemory(cf =>
                {
                    cf.EnableMessageHeaders = false;
                    cf.EnableMessageSerialization = false;
                })
                .AutoDeclareFrom(assembly, consumerTypeFilter: t => t.Namespace != typeof(DeliverNotification).Namespace))
            // The delivery queue (ADR-0013): a Redis list, or a non-blocking memory topic in local runs and tests.
            .AddChildBus("Delivery", child =>
            {
                if (redis is null)
                {
                    child.WithProviderMemory(cf =>
                        {
                            cf.EnableBlockingPublish = false;
                            cf.EnableMessageSerialization = false;
                        })
                        .Produce<DeliverNotification>(x => x.DefaultTopic(DeliverNotification.QueueName))
                        .Consume<DeliverNotification>(x => x
                            .Topic(DeliverNotification.QueueName)
                            .WithConsumer<IConsumer<DeliverNotification>>()
                            .Instances(1));
                }
                else
                {
                    // The provider refuses an empty ConnectionString even with a factory; the factory shares the one connection.
                    child.WithProviderRedis(cfg =>
                        {
                            cfg.ConnectionString = redisConnectionString;
                            cfg.ConnectionFactory = () => redis.Value;
                        })
                        .Produce<DeliverNotification>(x => x.DefaultQueue(DeliverNotification.QueueName))
                        .Consume<DeliverNotification>(x => x
                            .Queue(DeliverNotification.QueueName)
                            .WithConsumer<IConsumer<DeliverNotification>>()
                            .Instances(1));
                }
            })
            // The Redis bus's serializer; both memory buses pass the message object as is.
            .AddJsonSerializer()
            .AddServicesFromAssembly(assembly));
    }

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