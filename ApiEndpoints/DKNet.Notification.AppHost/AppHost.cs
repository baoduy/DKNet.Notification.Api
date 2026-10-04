var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("Redis");

// The local mail catcher (DRK-2020 §3 Local run): it takes mail over STARTTLS only, on the developer certificate the
// machine already trusts, so the service checks its certificate as it checks any other. Its inbox is the "http"
// endpoint.
var mailpit = builder.AddContainer("Mailpit", "axllent/mailpit", "v1.27")
    .WithEndpoint(targetPort: 1025, name: "smtp")
    .WithHttpEndpoint(targetPort: 8025, name: "http")
    .WithEnvironment("MP_SMTP_REQUIRE_STARTTLS", "true")
    .WithHttpsDeveloperCertificate()
    .WithHttpsCertificateConfiguration(context =>
    {
        context.EnvironmentVariables["MP_SMTP_TLS_CERT"] = context.CertificatePath;
        context.EnvironmentVariables["MP_SMTP_TLS_KEY"] = context.KeyPath;
        return Task.CompletedTask;
    });
var smtp = mailpit.GetEndpoint("smtp");

// The (name, projectPath) overload takes a plain path string, which survives sourceName
// substitution as text — unlike AddProject<DKNet.Notification_Api>, whose generated Projects.* identifier
// (derived from the .csproj file name with '.'/'-' replaced by '_') can disagree with the
// template engine's own text substitution for a name containing a dot (e.g. "DKNet.Accounts").
// The rest of the email settings come from the API's appsettings.Development.json.
builder.AddProject("Api", "../DKNet.Notification.Api/DKNet.Notification.Api.csproj")
    .WithReference(cache, "Redis")
    .WithEnvironment("Notifications__Email__Smtp__Host", smtp.Property(EndpointProperty.Host))
    .WithEnvironment("Notifications__Email__Smtp__Port", smtp.Property(EndpointProperty.Port))
    .WaitFor(cache)
    .WaitFor(mailpit);

await builder.Build().RunAsync();
