using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DKNet.Notification.App.TestSupport;

/// <summary>
/// Fake authentication scheme standing in for the real JWT bearer scheme (which needs a live MS Graph token
/// to validate), so the "authorization required" path can be exercised in-process. Every request is
/// unconditionally authenticated as <see cref="CallerName" />.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";
    public const string CallerName = "test-authenticated-caller";

    /// <summary>
    /// The claim <c>PrincipalProvider</c> reads as <c>ProfileId</c> and returns from <c>GetOwnershipKey()</c>
    /// (this value's string form), not <see cref="CallerName" />. A real token carries this as its
    /// <c>sub</c>/<c>oid</c> claim.
    /// </summary>
    public static readonly Guid CallerProfileId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, CallerName),
                new Claim(ClaimTypes.NameIdentifier, CallerProfileId.ToString())
            ],
            SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Registers this scheme as the default authenticate/challenge scheme, overriding whatever the host's own
    /// <c>AddAuthConfig</c> configured. Call from a test factory's <c>ConfigureTestServices</c> override.
    /// </summary>
    public static void Register(IServiceCollection services) =>
        services.AddAuthentication(SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(SchemeName, _ => { });
}
