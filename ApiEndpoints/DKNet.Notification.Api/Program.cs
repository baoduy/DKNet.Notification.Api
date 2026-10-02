using DKNet.Notification.Api.Configs;
using DKNet.Notification.Api.Configs.AzureAppConfig;
using SharpGrip.FluentValidation.AutoValidation.Endpoints.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Rebind features after potentially loading from Azure App Configuration
var feature = builder.Configuration.GetSection(FeatureOptions.Name).Get<FeatureOptions>() ?? new FeatureOptions();

builder.AddLogConfig(feature)
    .AddAzureAppConfig(feature);

builder.AddFluentValidationConfig();

// Add services to the container.
builder.Services
    .AddOptions(builder.Configuration)
    .AddAppConfig(feature, builder.Configuration)
    // Populates [FromClaim] request members before validation and before the handler, from the
    // authenticated caller's own claims.
    .AddContextualRequestPopulation();

await builder.Build()
    .UseAppConfig(a => a.UseEndpointConfigs(o =>
    {
        o.RequireAuthorization = feature.RequireAuthorization;
        o.EnableVersioning = feature.EnableVersioning;
        o.ConfigureGroup = (group, _) => group.AddFluentValidationAutoValidation();
    }, typeof(Program).Assembly));

//This Startup endpoint for Unit Tests
namespace DKNet.Notification.Api
{
    public class Program;
}