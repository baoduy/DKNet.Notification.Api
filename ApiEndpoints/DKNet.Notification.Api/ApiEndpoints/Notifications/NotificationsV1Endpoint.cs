using System.Diagnostics;
using DKNet.AspCore.Idempotency;
using DKNet.Notification.Api.Configs.Auth;
using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.Domains.Notifications;
using FluentValidation.Results;
using SharpGrip.FluentValidation.AutoValidation.Endpoints.Results;

namespace DKNet.Notification.Api.ApiEndpoints.Notifications;

/// <summary>
///     <c>POST /v1/notifications</c>. Steps run in order: sign-in and permission (the group scope), the body
///     (<see cref="SendNotificationBodyFilter" />), the idempotency key, then the template, the channel, the
///     recipient, the rendering and the queue (<see cref="SendNotificationService" />).
/// </summary>
[EndpointGroupScope(SendPermission.Name)]
internal sealed class NotificationsV1Endpoint : IEndpointConfig
{
    #region Fields

    /// <summary>How long a caller waits before it repeats a call refused with a full queue.</summary>
    private const string RetryAfterSeconds = "30";

    #endregion

    #region Properties

    public string GroupEndpoint => "/notifications";

    #endregion

    #region Methods

    public void Map(RouteGroupBuilder group) =>
        group.MapPost(string.Empty, Send)
            .Accepts<SendNotificationRequest>("application/json")
            .Produces<SendNotificationResponse>(StatusCodes.Status202Accepted)
            // Registered first, so it runs outside the idempotency filter: a refused body holds no key.
            .AddEndpointFilter<SendNotificationBodyFilter>()
            .RequiredIdempotentKey();

    /// <summary>The caller id of a call that passed step 1.</summary>
    internal static string CallerOf(HttpContext context) =>
        CallerIdentity.Resolve(context.User)
        // Unreachable: the send permission refuses a token with no caller claim (401) before any step runs.
        ?? throw new InvalidOperationException("The call carries no caller claim.");

    /// <summary>The trace id an error body carries, so a log entry can name the same one.</summary>
    internal static string TraceIdOf(HttpContext context) => Activity.Current?.Id ?? context.TraceIdentifier;

    // Runs inside the idempotency filter: a refusal is not a 2xx, so the key stays held for 30 seconds. Every
    // answer carries its status code, so the filter keeps only the 202.
    private static IResult Send(
        SendNotificationBody body,
        [FromServices] SendNotificationService service,
        [FromServices] IFluentValidationAutoValidationResultFactory problems,
        HttpContext context)
    {
        // SendNotificationBodyFilter lets only a valid body through.
        var notification = service.Send(body.Request!, CallerOf(context), TraceIdOf(context));
        if (notification.Status != NotificationStatus.Rejected)
        {
            // Queued or Skipped: the same answer, so a caller cannot tell them apart.
            return TypedResults.Accepted((string?)null, new SendNotificationResponse(notification.NotificationId));
        }

        if (string.Equals(notification.ErrorCode, NotificationErrorCodes.QueueFull, StringComparison.Ordinal))
        {
            context.Response.Headers.RetryAfter = RetryAfterSeconds;
        }

        return problems.CreateResult(
            EndpointFilterInvocationContext.Create(context),
            new ValidationResult([
                // The service rejects with a code and a field every time.
                new ValidationFailure(notification.ErrorField!, ErrorMessage(notification.ErrorCode))
                {
                    ErrorCode = notification.ErrorCode
                }
            ]));
    }

    private static string ErrorMessage(string? code) => code switch
    {
        NotificationErrorCodes.TemplateNotFound => "No template is registered with this id.",
        NotificationErrorCodes.RecipientMissing => "The email needs a recipient.",
        NotificationErrorCodes.RecipientInvalid => "The recipient must be exactly 1 address of at most 254 characters.",
        NotificationErrorCodes.ParameterMissing => "A template token has no parameter.",
        _ => "The delivery queue is full. Try again later."
    };

    #endregion
}
