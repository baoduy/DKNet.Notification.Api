using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Design;
using DKNet.Notification.Infra.Extensions;

namespace DKNet.Notification.Infra.Contexts;

[ExcludeFromCodeCoverage]
internal sealed class DbContextFactory : IDesignTimeDbContextFactory<CoreDbContext>
{
    #region Methods

    public CoreDbContext CreateDbContext(string[] args) => BuildServiceProvider(args).GetRequiredService<CoreDbContext>();

    /// <summary>
    /// Builds the same design-time service provider <see cref="CreateDbContext"/> resolves
    /// <see cref="CoreDbContext"/> from. <c>UseAutoDataSeeding</c>'s post-migration hook publishes
    /// domain events through <see cref="DKNet.Notification.Infra.Services.EventPublisher"/>, which needs an
    /// <c>IMessageBus</c> to activate — without one, <c>dotnet ef database update</c> throws while
    /// resolving the seeding hook. Calls <see cref="ServiceBusSetup.AddMemoryBus"/> to register the
    /// same in-memory-only bus the runtime path uses, still without the Azure child bus and without
    /// <c>AddSlimBusEfCoreInterceptor</c>, so nothing is ever published to a real broker at design time.
    /// The scanned assembly here is <c>typeof(InfraSetup).Assembly</c> rather than the app assembly the
    /// runtime path passes — <c>DKNet.Notification.Infra</c> cannot reference <c>DKNet.Notification.Api</c>, so the app
    /// assembly is unreachable at design time. Today that's harmless: the only seeded aggregate,
    /// <c>PurchaseOrder</c>, raises no events. A future <c>DataSeedingConfiguration&lt;T&gt;</c> over an
    /// event-raising entity would need a declared producer for its event to be handled here.
    /// </summary>
    internal static IServiceProvider BuildServiceProvider(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ConnectionStrings:AppDb"] =
                        "Host=localhost;Username=postgres;Password=postgres;Database=SampleDb"
                })
            .Build();

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddInfraServices()
            .AddSlimMessageBus(mbb => mbb.AddJsonSerializer().AddMemoryBus(typeof(InfraSetup).Assembly))
            .AddLogging()
            .BuildServiceProvider();
    }

    #endregion
}