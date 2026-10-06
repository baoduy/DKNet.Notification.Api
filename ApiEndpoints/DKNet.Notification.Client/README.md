# DKNet.Notification.Client

A typed .NET client for the DKNet Notification service: send a notification from a registered template and read
its status with two calls, without writing routes or JSON.

## Install

The package lives in GitHub Packages of `baoduy/DKNet.Notification.Api`, not NuGet.org. That feed has no anonymous
read: restoring it needs a GitHub personal access token with the `read:packages` scope. Add the feed once, with the
token taken from an environment variable so it never lands in a file you commit:

```bash
dotnet nuget add source https://nuget.pkg.github.com/baoduy/index.json \
  --name github-baoduy \
  --username YOUR_GITHUB_USERNAME \
  --password $GH_PACKAGES_TOKEN \
  --store-password-in-clear-text

dotnet add package DKNet.Notification.Client
```

## Register

The client adds no credential of its own. Register it with the service address only:

```csharp
services.AddNotificationClient(new Uri("https://notifications.example.com"));
```

Or chain your own token handler, a `DelegatingHandler` registered in the same container, onto every request:

```csharp
services.AddTransient<BearerTokenHandler>();
services.AddNotificationClient(new Uri("https://notifications.example.com"), typeof(BearerTokenHandler));
```

The client never retries: one call sends one request.

## Send

```csharp
var client = provider.GetRequiredService<INotificationClient>();
var sent = await client.SendAsync(
    new SendNotificationRequest("email", "account-opened", new Dictionary<string, string>
    {
        ["to"] = "jane@example.com",
        ["customerName"] = "Jane",
        ["accountNumber"] = "0012345678"
    }),
    idempotencyKey: "onboard-0012345678");
// sent.NotificationId
```

The idempotency key is sent exactly as given. Sending again with the same key returns the same notification id.

### When the service refuses

Every answer that is not a success throws `NotificationApiException`, with the status and the service's error list:

```csharp
try
{
    await client.SendAsync(request, idempotencyKey);
}
catch (NotificationApiException ex)
{
    // ex.StatusCode, ex.Errors[0].Code / .Field / .Message — for example 400 TEMPLATE_NOT_FOUND or 503 QUEUE_FULL
}
```

A refusal with an empty body, such as 401 or 403, has an empty `Errors` list. A connection failure or a timeout is
not wrapped: it reaches you as the `HttpRequestException` or `TaskCanceledException` it is.

## Read status

```csharp
var status = await client.GetStatusAsync(sent.NotificationId);
// status.Status is NotificationStatus.Pending, Success or Failed; status.IdempotencyKey is the key it was sent with
```

An unknown, expired or another caller's id throws `NotificationApiException` with status 404 and code
`NOTIFICATION_NOT_FOUND`.
