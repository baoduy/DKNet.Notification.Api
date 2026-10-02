using Microsoft.OpenApi;
using DKNet.Notification.Api.Configs.Auth;
using DKNet.Notification.Api.Configs.Healthz;

namespace DKNet.Notification.Api.Configs.Swagger;

[SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters")]
[ExcludeFromCodeCoverage]
internal static class SwaggerConfig
{
    #region Fields

    /// <summary>Route the interactive documentation UI is mapped at; SecurityHeadersConfig relaxes its CSP.</summary>
    public const string DocsPath = "/docs";

    private static readonly string[] ExcludeFromPublic = ["internal", "static"];

    #endregion

    #region Methods

    public static WebApplication UseOpenApiDoc(this WebApplication app)
    {
        if (!app.Services.IsConfigAdded(nameof(SwaggerConfig))) return app;

        var openApi = app.MapOpenApi();
        var docs = app.MapScalarApiReference(DocsPath, c =>
            c.WithTitle($"{SharedConsts.ApiName} API")
                .WithTheme(ScalarTheme.Default)
                //.WithOpenApiRoutePattern("{documentName}.json")
                .AddPreferredSecuritySchemes("Bearer")
                .AddHttpAuthentication("Bearer", b => b.Token = "bearer token")
        );

        // Outside Development, the document and its UI require an authenticated caller — independent of
        // EnableSwagger. Only enforceable when AuthConfig actually wired UseAuthorization(); with
        // RequireAuthorization off there is no authorization middleware to evaluate the requirement.
        if (!app.Environment.IsDevelopment() && app.Services.IsConfigAdded(nameof(AuthConfig)))
        {
            openApi.RequireAuthorization();
            docs.RequireAuthorization();
        }

        app.Logger.LogInformation("{Feature} enabled", nameof(SwaggerConfig));
        return app;
    }

    /// <summary>
    ///     <c>MapHealthChecks</c> registers a raw <see cref="RequestDelegate" /> pipeline, so ApiExplorer never
    ///     sees the health routes (no <c>MethodInfo</c> metadata) and no endpoint convention —
    ///     <c>WithName</c>/<c>WithTags</c>/<c>WithGroupName</c> — can put them in the document. They are described
    ///     here by hand and must be kept in step with <see cref="HealthzConfig.UseHealthzConfig" />. The same
    ///     public probe is also mapped at "/", left undocumented on purpose to keep the root out of the API surface.
    /// </summary>
    private static void AddHealthzPaths(OpenApiDocument doc)
    {
        doc.Paths["/healthz"] = HealthPath(doc,
            "Liveness probe",
            "Aggregated health status only — no per-check detail. Anonymous.",
            new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["status"] = new OpenApiSchema { Type = JsonSchemaType.String }
                }
            });

        doc.Paths["/healthz/detail"] = HealthPath(doc,
            "Health report",
            "Full per-check report. Requires an authenticated caller when authorization is enabled.",
            new OpenApiSchema { Type = JsonSchemaType.Object });
    }

    private static OpenApiPathItem HealthPath(
        OpenApiDocument doc,
        string summary,
        string description,
        IOpenApiSchema schema) =>
        new()
        {
            Operations = new Dictionary<HttpMethod, OpenApiOperation>
            {
                [HttpMethod.Get] = new()
                {
                    Summary = summary,
                    Description = description,
                    Tags = new HashSet<OpenApiTagReference> { new("Health", doc) },
                    Responses = new OpenApiResponses
                    {
                        ["200"] = new OpenApiResponse
                        {
                            Description = "Healthy or Degraded.",
                            Content = new Dictionary<string, OpenApiMediaType>
                            {
                                ["application/json"] = new() { Schema = schema }
                            }
                        },
                        ["503"] = new OpenApiResponse { Description = "Unhealthy." }
                    }
                }
            }
        };

    #endregion

    extension(IServiceCollection services)
    {
        public IServiceCollection AddOpenApiDoc()
        {
            services.AddOpenApiDocVersion("v1", true);

            services.MarkConfigAdded(nameof(SwaggerConfig));
            return services;
        }

        private IServiceCollection AddOpenApiDocVersion(string name, bool includeInternal = false)
        {
            return services.AddOpenApi(name,
                c =>
                {
                    //The OpenAPI version 3.1 is not compatible with APIM yet.
                    c.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
                    c.ShouldInclude = description =>
                    {
                        return includeInternal || !ExcludeFromPublic.Any(s =>
                            description.RelativePath!.Contains(s, StringComparison.OrdinalIgnoreCase));
                    };

                    c.AddDocumentTransformer((doc, ctx, _) =>
                    {
                        doc.Info.Title = $"{SharedConsts.ApiName} API {name} Version";
                        //doc.Servers!.AddRange();

                        var paths = new OpenApiPaths();
                        foreach (var openApiPath in doc.Paths)
                        {
                            var key = openApiPath.Key.Replace("v{version}", "v1",
                                StringComparison.OrdinalIgnoreCase);
                            paths.Add(key, openApiPath.Value);
                        }

                        doc.Paths = paths;

                        if (ctx.ApplicationServices.IsConfigAdded(nameof(HealthzConfig)))
                        {
                            AddHealthzPaths(doc);
                        }

                        return Task.CompletedTask;
                    });
                });
        }
    }
}