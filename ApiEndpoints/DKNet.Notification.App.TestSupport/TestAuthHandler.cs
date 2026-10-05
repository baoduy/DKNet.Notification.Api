using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DKNet.Notification.App.TestSupport;

/// <summary>
/// Fake authentication scheme standing in for the real JWT bearer scheme (which needs a live MS Graph token
/// to validate), so the "authorization required" path can be exercised in-process. A request without the
/// <see cref="ClaimsHeader" /> is unconditionally authenticated as <see cref="CallerName" />.
/// </summary>
/// <remarks>
/// A request that sends <see cref="ClaimsHeader" /> describes its own token instead:
/// <list type="bullet">
/// <item>no <c>Authorization</c> header — no token, so the caller stays unauthenticated;</item>
/// <item><c>Authorization: Bearer <see cref="InvalidToken" /></c> — a token that fails validation;</item>
/// <item>any other <c>Authorization</c> value — a valid token holding exactly the claims the header lists, as
/// <c>type=value</c> pairs separated by <c>;</c> (a claim type may repeat, one value per claim).</item>
/// </list>
/// </remarks>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";
    public const string CallerName = "test-authenticated-caller";

    /// <summary>The test-only header that lists the claims of a per-request token.</summary>
    public const string ClaimsHeader = "X-Test-Claims";

    /// <summary>The bearer token value this scheme treats as invalid.</summary>
    public const string InvalidToken = "invalid-token";

    /// <summary>
    /// The caller's user id claim (this value's string form), not <see cref="CallerName" />. A real token
    /// carries this as its <c>sub</c>/<c>oid</c> claim.
    /// </summary>
    public static readonly Guid CallerProfileId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ClaimsHeader, out var claimList))
        {
            return Task.FromResult(Success(
            [
                new Claim(ClaimTypes.Name, CallerName),
                new Claim(ClaimTypes.NameIdentifier, CallerProfileId.ToString())
            ]));
        }

        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authorization))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (string.Equals(authorization, $"Bearer {InvalidToken}", StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Fail("The token is invalid."));
        }

        var claims = string.Join(';', claimList.ToArray())
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Select(parts => new Claim(parts[0], parts[1]))
            .ToArray();
        return Task.FromResult(Success(claims));
    }

    private static AuthenticateResult Success(Claim[] claims) =>
        AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)),
            SchemeName));

    /// <summary>
    /// Registers this scheme as the default authenticate/challenge scheme, overriding whatever the host's own
    /// <c>AddAuthConfig</c> configured. Call from a test factory's <c>ConfigureTestServices</c> override.
    /// </summary>
    public static void Register(IServiceCollection services) =>
        services.AddAuthentication(SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(SchemeName, _ => { });
}
