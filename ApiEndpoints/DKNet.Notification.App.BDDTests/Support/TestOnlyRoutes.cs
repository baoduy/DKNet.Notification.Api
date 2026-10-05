using FluentResults;
using DKNet.AspCore.Extensions.Responses;
using DKNet.Notification.AppServices.Share;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace DKNet.Notification.App.BDDTests.Support;

/// <summary>
/// Two routes registered only in the BDD test host, so the ErrorHandling scenarios need no business route. The
/// library's own start-up filter (registered by <c>AddErrorResponses</c> in <c>Program.cs</c>, ahead of this one)
/// wraps them in its exception handler, so both answer through the service's registered error setting.
/// </summary>
public sealed class TestOnlyRoutes : IStartupFilter
{
    /// <summary>Raises a genuine unhandled exception.</summary>
    public const string UnexpectedErrorPath = "/test-only/unexpected-error";

    /// <summary>Answers a command failure whose error code carries the "precondition." prefix.</summary>
    public const string PreconditionFailurePath = "/test-only/precondition-failure";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Map(UnexpectedErrorPath, route => route.Run(_ =>
            throw new InvalidOperationException("Test-only route: deliberate unexpected error.")));

        app.Map(PreconditionFailurePath, route => route.Run(context =>
            Result.Fail(new Error("Test-only route: deliberate precondition failure.")
                    .WithMetadata("Code", $"{PreconditionCodes.Prefix}test-only"))
                .Response()
                .ExecuteAsync(context)));

        next(app);
    };
}
