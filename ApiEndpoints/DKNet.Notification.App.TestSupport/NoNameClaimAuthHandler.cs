using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DKNet.Notification.App.TestSupport;

/// <summary>
/// Fake authentication scheme standing in for a real caller who is authenticated but whose token carries no
/// <see cref="ClaimTypes.Name" /> claim — e.g. a service-to-service token or an identity provider that omits it.
/// Used to prove <c>[FromClaim]</c> population holds its declared member at its default when the caller is
/// authenticated but the claim itself is missing.
/// </summary>
public sealed class NoNameClaimAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "NoNameClaimTestScheme";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Authenticated, but deliberately no ClaimTypes.Name claim.
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "no-name-claim-caller")], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Registers this scheme as the default authenticate/challenge scheme, overriding whatever the host's own
    /// <c>AddAuthConfig</c> configured. Call from a test factory's <c>ConfigureTestServices</c> override.
    /// </summary>
    public static void Register(IServiceCollection services) =>
        services.AddAuthentication(SchemeName)
            .AddScheme<AuthenticationSchemeOptions, NoNameClaimAuthHandler>(SchemeName, _ => { });
}
