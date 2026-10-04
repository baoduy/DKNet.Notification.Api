using Microsoft.AspNetCore.Authorization;

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

        // Claims keep their token names (scp, roles, azp, appid), the names the send permission and the caller id read.
        services.AddAuthentication()
            .AddJwtBearer(o => o.MapInboundClaims = false);

        services.AddAuthorization(options =>
        {
            // Default deny: any endpoint not explicitly declared anonymous requires an authenticated caller.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            // The policy [EndpointGroupScope] names on the send endpoint.
            options.AddPolicy(
                SendPermission.Name,
                policy => policy.RequireAuthenticatedUser().RequireAssertion(c => SendPermission.IsGranted(c.User)));
        });

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, CallerClaimResultHandler>();

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

        return app;
    }

    #endregion
}