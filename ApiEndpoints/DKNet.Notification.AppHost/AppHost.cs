using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DKNet.Notification.AppHost.SampleData;

var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("Redis");
var postgres = builder.AddPostgres("Postgres");

var apDb = postgres
    .AddDatabase("AppDb");

// The (name, projectPath) overload takes a plain path string, which survives sourceName
// substitution as text — unlike AddProject<DKNet.Notification_Api>, whose generated Projects.* identifier
// (derived from the .csproj file name with '.'/'-' replaced by '_') can disagree with the
// template engine's own text substitution for a name containing a dot (e.g. "DKNet.Accounts").
builder.AddProject("Api", "../DKNet.Notification.Api/DKNet.Notification.Api.csproj")
    .WithReference(cache, "Redis")
    .WithReference(apDb, "AppDb")

    //.WaitFor(bus)
    .WaitFor(cache)
    .WaitFor(apDb);

var recordsPerEntity = builder.Configuration.GetValue("SampleData:RecordsPerEntity", 10000);

builder.Eventing.Subscribe<AfterResourcesCreatedEvent>(async (@event, cancellationToken) =>
{
    var logger = @event.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SampleDataGenerator");

    var connectionString = await apDb.Resource.ConnectionStringExpression.GetValueAsync(cancellationToken);
    if (string.IsNullOrEmpty(connectionString))
    {
        logger.LogWarning("Sample-data generation skipped: no connection string was resolved for the AppDb resource.");
        return;
    }

    await SampleDataGenerator.RunAsync(connectionString, recordsPerEntity, logger, cancellationToken);
});

await builder.Build().RunAsync();