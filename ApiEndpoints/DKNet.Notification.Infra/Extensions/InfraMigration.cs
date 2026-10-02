using System.Diagnostics.CodeAnalysis;
using DKNet.EfCore.Extensions.Extensions;
using DKNet.Notification.Infra.Contexts;

namespace DKNet.Notification.Infra.Extensions;

/// <summary>
///
/// </summary>
[ExcludeFromCodeCoverage]
public static class InfraMigration
{
    #region Methods

    /// <summary>
    /// Migrates the database to the latest version. This method should be called during application startup to ensure that the database schema is up to date before the application starts handling requests.
    /// </summary>
    /// <param name="connectionString"></param>
    public static async Task MigrateDb(string connectionString)
    {
        //Db migration
        await using var db = new CoreDbContext(
            new DbContextOptionsBuilder<CoreDbContext>()
                .UseAutoConfigModel([typeof(CoreDbContext).Assembly, typeof(Sequences).Assembly])
                .UseNpgsqlWithMigration(connectionString)
                .UseAutoDataSeeding([typeof(InfraSetup).Assembly])
                .Options);

        // Seeding runs as part of MigrateAsync via UseAutoDataSeeding above.
        await db.Database.MigrateAsync();
    }

    #endregion
}