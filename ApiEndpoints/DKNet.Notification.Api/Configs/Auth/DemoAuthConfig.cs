using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.Api.Configs.Auth;

/// <summary>
///     Development/demonstration-only authentication scheme (R1): every request is authenticated as the fixed,
///     self-evidently fake identity <see cref="SharedConsts.DemoAccount" /> instead of validating a real token.
/// </summary>
/// <remarks>
///     Registered only by <see cref="DemoAuthConfig.AddDemoAuthConfig" />, which itself is only reached when
///     <c>EnableDemoAuthentication</c> is on and <c>RequireAuthorization</c> is off — never the default, never
///     combined with real authorization (R2).
/// </remarks>
internal sealed class DemoAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Demo";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // NameIdentifier is the demo identity's own subject, distinct from ByUser's ClaimTypes.Name — it keeps
        // pre-existing seeded-ownership tests (OwnedBy/CreatedBy == SharedConsts.SystemAccount) passing.
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, SharedConsts.DemoAccount),
                new Claim(ClaimTypes.NameIdentifier, SharedConsts.SystemAccount)
            ],
            SchemeName);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
///     Provides the extension method that registers the built-in demonstration authentication provider.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class DemoAuthConfig
{
    #region Methods

    /// <summary>
    ///     Adds the demonstration authentication scheme as the default authenticate/challenge scheme. No
    ///     authorization services or policies are registered — the demonstration provider never gates access,
    ///     it only supplies an acting-user identity.
    /// </summary>
    public static IServiceCollection AddDemoAuthConfig(this IServiceCollection services)
    {
        services.MarkConfigAdded(nameof(DemoAuthConfig));

        services.AddAuthentication(DemoAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>(
                DemoAuthenticationHandler.SchemeName, _ => { });

        return services;
    }

    #endregion
}
