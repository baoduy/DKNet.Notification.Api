using DKNet.EfCore.Extensions.Serialization;
using DKNet.Notification.Infra.Contexts;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace DKNet.Notification.Api.Configs;

[ExcludeFromCodeCoverage]
internal static class ServiceConfigs
{
    #region Methods

    public static IServiceCollection AddAllAppServices(
        this IServiceCollection services,
        IConfiguration configuration,
        FeatureOptions features)
    {
        services
            .AddSingleton<IHttpContextAccessor, HttpContextAccessor>()
            .AddSingleton<ISensitiveDataPrincipalAccessor, HttpContextSensitiveDataPrincipalAccessor>()
            .AddScoped<IPrincipalProvider, PrincipalProvider>()
            // Also wires DKNet's DataOwnerHook onto CoreDbContext: it stamps CreatedBy/CreatedOn from
            // IDataOwnerProvider on save, never from a request property — a generated create request can
            // never set the acting user (DRK-715 R1).
            .AddDataOwnerProvider<CoreDbContext, PrincipalProvider>()
            // Stamps CreatedBy/UpdatedBy from the signed-in user's subject claim, independently of
            // OwnedBy (still the tenant ownership key above) — DRK-1466.
            .AddCurrentUserProvider<CoreDbContext, PrincipalProvider>();

        services
            .AddAppServices()
            .AddInfraServices()

            //Service Bus
            .AddServiceBus(configuration, typeof(AppSetup).Assembly, features);

        return services;
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

        // ConfigureHttpJsonOptions above has no service-provider access, so the role-aware sensitive-data
        // opt-in (needs ISensitiveDataPrincipalAccessor from DI) goes through this factory registration
        // instead — same JsonOptions instance, applied once at start-up, per the DKNet doc's recipe.
        services.AddSingleton<IConfigureOptions<JsonOptions>>(sp =>
            new ConfigureOptions<JsonOptions>(op =>
                op.SerializerOptions.UseRoleAwareSensitiveData(
                    sp.GetRequiredService<ISensitiveDataPrincipalAccessor>())));

        return services;
    }

    #endregion
}