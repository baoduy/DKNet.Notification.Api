using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DKNet.Notification.Api.ApiEndpoints.AutomatedSample;

namespace DKNet.Notification.App.TestSupport;

/// <summary>
/// Fake authentication scheme standing in for a real caller whose subject claims vary from request to
/// request within the SAME test host — every other fixture's auth handler bakes one fixed identity into the
/// scheme, so proving that two DIFFERENT authenticated callers get isolated ownership keys (and cannot read
/// each other's rows) needs a handler that can be more than one caller across the same <c>HttpClient</c>.
/// Each request supplies its own subject via headers rather than a shared mutable field, so it stays correct
/// even if requests ever run concurrently.
/// </summary>
public sealed class MultiSubjectAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "MultiSubjectTestScheme";

    /// <summary>Maps to a <see cref="ClaimTypes.NameIdentifier"/> claim when present.</summary>
    public const string SubjectHeaderName = "X-Test-Subject";

    /// <summary>Maps to an <c>oid</c> claim when present — takes precedence over <see cref="SubjectHeaderName"/>.</summary>
    public const string ObjectIdHeaderName = "X-Test-Oid";

    /// <summary>Comma-separated roles, mapped to one <see cref="ClaimTypes.Role"/> claim per entry.</summary>
    public const string RolesHeaderName = "X-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Every products.* scope, always — this handler's own tests (ownership isolation, sensitive-data
        // role filtering) exercise a different axis than Product's per-route scopes (DRK-1386 §5) and never
        // vary it, so unlike TestAuthHandler there is no header to override it with.
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "multi-subject-caller"),
            new("scp", string.Join(' ', ProductScopes.All))
        };

        if (Request.Headers.TryGetValue(ObjectIdHeaderName, out var oid) && !string.IsNullOrEmpty(oid))
        {
            claims.Add(new Claim("oid", oid!));
        }

        if (Request.Headers.TryGetValue(SubjectHeaderName, out var subject) && !string.IsNullOrEmpty(subject))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, subject!));
        }

        if (Request.Headers.TryGetValue(RolesHeaderName, out var roles) && !string.IsNullOrEmpty(roles))
        {
            foreach (var role in roles.ToString()
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Registers this scheme as the default authenticate/challenge scheme, overriding whatever the host's own
    /// <c>AddAuthConfig</c> configured. Call from a test factory's <c>ConfigureTestServices</c> override.
    /// </summary>
    public static void Register(IServiceCollection services) =>
        services.AddAuthentication(SchemeName)
            .AddScheme<AuthenticationSchemeOptions, MultiSubjectAuthHandler>(SchemeName, _ => { });
}
