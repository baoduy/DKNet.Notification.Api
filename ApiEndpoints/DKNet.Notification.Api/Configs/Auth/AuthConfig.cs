using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using DKNet.Notification.Api.ApiEndpoints.AutomatedSample;

namespace DKNet.Notification.Api.Configs.Auth;

/// <summary>
///     Provides extension methods for configuring authentication and authorization in an ASP.NET Core application.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class AuthConfig
{
    #region Methods

    /// <summary>
    ///     Adds authentication and authorization services to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add the authentication and authorization services to.</param>
    /// <returns>The updated <see cref="IServiceCollection" /> instance.</returns>
    /// <remarks>
    ///     This method configures the application to use JWT (JSON Web Token) Bearer authentication.
    ///     The token signature is validated against the issuer metadata from the
    ///     <c>Authentication:Schemes:Bearer:MetadataAddress</c> configuration.
    /// </remarks>
    public static IServiceCollection AddAuthConfig(this IServiceCollection services)
    {
        services.MarkConfigAdded(nameof(AuthConfig));

        services.AddAuthentication()
            .AddJwtBearer();

        services.AddAuthorization(options =>
        {
            // Default deny: any endpoint not explicitly declared anonymous requires an authenticated caller.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            // TODO: Replace "sample-scope" with the actual scope value from your identity provider,
            //       then apply the policy to an endpoint with .RequireAuthorization(HasScopeRequirement.PolicyName).
            options.AddPolicy(
                HasScopeRequirement.PolicyName,
                policy => policy.Requirements.Add(new HasScopeRequirement("sample-scope")));

            // Product's per-route scopes (DRK-1386 §5): one policy per scope, the scope value doubling
            // as its own policy name, so a route can call .RequireAuthorization(ProductScopes.Read) etc.
            foreach (var scope in ProductScopes.All)
            {
                options.AddPolicy(scope, policy => policy.Requirements.Add(new HasScopeRequirement(scope)));
            }
        });

        // Sample IClaimsTransformation: enriches the user principal after authentication.
        // TODO: Replace SampleClaimsTransformation with your real implementation or remove if not needed.
        services.AddScoped<IClaimsTransformation, SampleClaimsTransformation>();

        // Sample IAuthorizationHandler: evaluates HasScopeRequirement.
        // TODO: Replace HasScopeHandler with your real handler(s) or remove if not needed.
        services.AddScoped<IAuthorizationHandler, HasScopeHandler>();

        return services;
    }

    /// <summary>
    ///     Configures the specified <see cref="WebApplication" /> to use the added authentication and authorization services.
    /// </summary>
    /// <param name="app">The <see cref="WebApplication" /> to configure.</param>
    /// <returns>The updated <see cref="WebApplication" /> instance.</returns>
    /// <remarks>
    ///     This method enables authentication and authorization middleware only if the authentication configuration has been
    ///     added to the services.
    /// </remarks>
    public static WebApplication UseAuthConfig(this WebApplication app)
    {
        if (app.Services.IsConfigAdded(nameof(AuthConfig)))
        {
            app.UseAuthentication();
            app.UseAuthorization();
            app.Logger.LogInformation("{Feature} enabled", nameof(AuthConfig));
        }

        if (app.Services.IsConfigAdded(nameof(DemoAuthConfig)))
        {
            app.UseAuthentication();
            app.Logger.LogInformation("{Feature} enabled", nameof(DemoAuthConfig));
        }

        return app;
    }

    #endregion
}