using DKNet.Notification.AppServices.Notifications;
using FluentValidation;
using FluentValidation.Results;
using SharpGrip.FluentValidation.AutoValidation.Endpoints.Results;

namespace DKNet.Notification.Api.ApiEndpoints.Notifications;

/// <summary>
///     Step 3 of a send call: the body. Registered before the idempotency filter, so a call refused here holds no
///     key. A refusal answers 413, or 400 <see cref="NotificationErrorCodes.InvalidRequest" /> with 1 log entry and
///     1 count.
/// </summary>
internal sealed class SendNotificationBodyFilter : IEndpointFilter
{
    #region Methods

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var body = context.GetArgument<SendNotificationBody>(0);
        if (body.IsTooLarge)
        {
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var services = context.HttpContext.RequestServices;
        var result = body.Request is null
            ? new ValidationResult([
                new ValidationFailure("body", "The body must be a JSON object whose parameter values are strings.")
                {
                    ErrorCode = NotificationErrorCodes.InvalidRequest
                }
            ])
            : await services.GetRequiredService<IValidator<SendNotificationRequest>>()
                .ValidateAsync(body.Request, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }

        services.GetRequiredService<SendNotificationService>().RejectInvalid(
            NotificationsV1Endpoint.CallerOf(context.HttpContext),
            NotificationsV1Endpoint.TraceIdOf(context.HttpContext));
        return services.GetRequiredService<IFluentValidationAutoValidationResultFactory>().CreateResult(context, result);
    }

    #endregion
}
