using System.Net;

namespace DKNet.Notification.Client;

/// <summary>One entry of a refusal's error list, exactly as the service sent it.</summary>
public sealed record NotificationApiError
{
    public string? Code { get; init; }

    public string? Field { get; init; }

    public required string Message { get; init; }
}

/// <summary>Thrown for every answer of the service that is not a success.</summary>
public sealed class NotificationApiException : Exception
{
    public NotificationApiException(HttpStatusCode statusCode, IReadOnlyList<NotificationApiError> errors, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<NotificationApiError> Errors { get; }
}
