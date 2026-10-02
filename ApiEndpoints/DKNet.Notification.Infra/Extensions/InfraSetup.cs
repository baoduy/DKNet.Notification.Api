using System.Diagnostics.CodeAnalysis;
using DKNet.EfCore.Extensions.Extensions;
using DKNet.EfCore.Hooks;
using DKNet.EfCore.Specifications;
using Microsoft.EntityFrameworkCore.Diagnostics;
using DKNet.Notification.Domains.Services;
using DKNet.Notification.Infra.Contexts;
using DKNet.Notification.Infra.Services;

namespace DKNet.Notification.Infra.Extensions;

/// <summary>
/// Registers infrastructure-layer services, repository/service implementations,
/// and database context configuration for the application.
/// </summary>
[ExcludeFromCodeCoverage]
public static class InfraSetup
{
    #region Methods

    /// <summary>
    /// Adds infrastructure dependencies, including infra services,
    /// domain event publishing, and the EF Core <see cref="CoreDbContext"/> setup.
    /// </summary>
    /// <param name="service">The service collection used to register dependencies.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddInfraServices(this IServiceCollection service)
    {
        service
            .AddScoped<IMembershipService, MembershipService>()
            .AddSpecRepo<CoreDbContext>()
            .AddEventPublisher<CoreDbContext, EventPublisher>()
            .AddDbContextWithHook<CoreDbContext>((sp, builder) =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                var conn = config.GetConnectionString(SharedConsts.DbConnectionString)!;

                builder.UseNpgsqlWithMigration(conn)
                    .UseAutoConfigModel([typeof(CoreDbContext).Assembly, typeof(Sequences).Assembly])
                    .UseAutoDataSeeding([typeof(InfraSetup).Assembly]);
            });

        return service;
    }

    /// <summary>
    /// Configures PostgreSQL options, migration metadata, query behavior, and retry settings
    /// for the current <see cref="DbContextOptionsBuilder"/>.
    /// </summary>
    /// <param name="builder">The options builder to configure.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The configured <see cref="DbContextOptionsBuilder"/>.</returns>
    internal static DbContextOptionsBuilder UseNpgsqlWithMigration(
        this DbContextOptionsBuilder builder,
        string connectionString)
    {
        builder.ConfigureWarnings(warnings =>
        {
            warnings.Log(RelationalEventId.PendingModelChangesWarning);
            //warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning);
        });
#if DEBUG
        builder.EnableDetailedErrors().EnableSensitiveDataLogging();
#endif

        return builder.UseNpgsql(
            connectionString,
            o => o
                .MinBatchSize(1)
                .MaxBatchSize(100)
                .MigrationsHistoryTable(nameof(CoreDbContext), DomainSchemas.Migration)
                .MigrationsAssembly(typeof(CoreDbContext).Assembly)
                .EnableRetryOnFailure()
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
    }

    #endregion
}