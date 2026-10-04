using DKNet.AspCore.Extensions.Responses;
using FluentValidation;

namespace DKNet.Notification.Api.Configs;

// Kept (not removed per the brief's conditional): ApiTests.AllConfigsClassesShouldBeStaticAndExcludedFromCodeCoverage
// requires every *Config class to carry this — an existing, repo-wide architecture invariant this
// change must not break. The new StatusCode/Customize branches are exercised end to end by the BDD
// precondition scenarios; they just don't count toward the coverage denominator, same as every other
// wiring-only Config class in this project.
[ExcludeFromCodeCoverage]
internal static class FluentValidationConfig
{
    #region Methods

    /// <summary>
    /// The template's one <see cref="ErrorResponseOptions" /> registration — it answers a failed
    /// command, refused validation input and an unhandled error alike (a route resolves it via
    /// <c>[FromServices]</c>). A refusal whose error carries a
    /// <see cref="PreconditionCodes.Prefix" />-prefixed code answers 409, a full delivery queue
    /// (<see cref="NotificationErrorCodes.QueueFull" />) answers 503; every other refusal keeps today's status
    /// (R4: the body carries only <c>traceId</c> and this service-chosen code, never a database message).
    /// </summary>
    public static WebApplicationBuilder AddFluentValidationConfig(this WebApplicationBuilder builder)
    {
        builder.Services.AddErrorResponses(o =>
        {
            o.StatusCode = ctx =>
                ctx.Errors.Any(e =>
                    e.Code is not null && e.Code.StartsWith(PreconditionCodes.Prefix, StringComparison.Ordinal))
                    ? StatusCodes.Status409Conflict
                    : ctx.Errors.Any(e => string.Equals(e.Code, NotificationErrorCodes.QueueFull, StringComparison.Ordinal))
                        ? StatusCodes.Status503ServiceUnavailable
                        : null;

            o.Customize = (problemDetails, ctx) =>
            {
                // Prefer a precondition. code over an ordinary coded rule mixed into the same refusal, so
                // "code" always names the rule the StatusCode above actually answered 409 for.
                var code = ctx.Errors.Select(e => e.Code)
                    .FirstOrDefault(c => c is not null && c.StartsWith(PreconditionCodes.Prefix, StringComparison.Ordinal))
                    ?? ctx.Errors.Select(e => e.Code).FirstOrDefault(c => c is not null);
                if (code is not null)
                {
                    problemDetails.Extensions["code"] = code;
                }
            };
        });
        builder.Services.AddValidatorsFromAssembly(typeof(PreconditionCodes).Assembly, includeInternalTypes: true);

        return builder;
    }

    #endregion
}