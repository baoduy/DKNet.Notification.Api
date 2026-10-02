var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("Redis");

// The (name, projectPath) overload takes a plain path string, which survives sourceName
// substitution as text — unlike AddProject<DKNet.Notification_Api>, whose generated Projects.* identifier
// (derived from the .csproj file name with '.'/'-' replaced by '_') can disagree with the
// template engine's own text substitution for a name containing a dot (e.g. "DKNet.Accounts").
builder.AddProject("Api", "../DKNet.Notification.Api/DKNet.Notification.Api.csproj")
    .WithReference(cache, "Redis")
    .WaitFor(cache);

await builder.Build().RunAsync();
