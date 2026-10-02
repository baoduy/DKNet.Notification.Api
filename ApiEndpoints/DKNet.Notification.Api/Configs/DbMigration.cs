using DKNet.Notification.Api.Configs.Jobs;

namespace DKNet.Notification.Api.Configs;

[ExcludeFromCodeCoverage]
internal static class DbMigration
{
    #region Methods

    /// <summary>
    ///     When <see cref="FeatureOptions.RunDbMigrationWhenAppStart" /> is set, runs the migration job in-process
    ///     before serving — in every environment and every build configuration (R6). A thin wrapper over the same
    ///     <see cref="MigrationJob" /> the "migration" argument dispatches to (§3 row 1), not a second
    ///     implementation of it.
    /// </summary>
    public static async Task RunMigrationAsync(this WebApplicationBuilder builder, FeatureOptions features)
    {
        if (!features.RunDbMigrationWhenAppStart)
        {
            return;
        }

        var exitCode = await MigrationJob.RunAsync(builder);
        if (exitCode != 0)
        {
            throw new InvalidOperationException("Startup database migration failed.");
        }
    }

    #endregion
}