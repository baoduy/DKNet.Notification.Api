# DKNet.Notification.Client — consumer guide

The `DKNet.Notification.Client` NuGet package lets .NET backend services send a templated notification
and read its delivery status without building HTTP routes or parsing response JSON.

This guide describes `dev` at commit `e99743b17fb94ce61f920e1c90644fd28c3fef4c`.
The installation steps apply once the first client release is published.

## ✨ Why use it?

- **Send and check the result.** `INotificationClient` covers the service's two caller routes;
  no method targets the operational `/healthz` route.
- **Keep sign-in in your application.** Register your own `DelegatingHandler` to add a bearer token;
  the package obtains no credential.
- **Handle refusals as data.** `NotificationApiException` exposes the HTTP status and the service's
  error entries, including the field that needs correcting.

## 🚀 Quick Start

Use a .NET 10 application. The package targets `net10.0` and takes Refit as a dependency.
The release workflow packs it with the service's computed release version and publishes to the
`baoduy` GitHub Packages feed.

Put this **credential-free** `nuget.config` in your calling application's folder:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github_baoduy" value="https://nuget.pkg.github.com/baoduy/index.json" />
  </packageSources>
</configuration>
```

Set `GH_PACKAGES_TOKEN` through your local secret manager or CI secret store to a GitHub personal
access token (classic) with `read:packages` and access to the package. Set `GH_PACKAGES_USERNAME`
to your GitHub username. Then, in that same shell:

```bash
export NuGetPackageSourceCredentials_github_baoduy="Username=${GH_PACKAGES_USERNAME};Password=${GH_PACKAGES_TOKEN};ValidAuthenticationTypes=Basic"
dotnet add package DKNet.Notification.Client
dotnet restore
```

The token stays in the process environment; the configuration file contains only feed addresses.
Do not write the token in source control or a committed `nuget.config`, or print the credential variable.
GitHub documents the [NuGet feed and token scopes](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry);
NuGet documents the [credential environment variable](https://learn.microsoft.com/nuget/consume-packages/consuming-packages-authenticated-feeds).

For a local Development service with authorization disabled, register with the address only.
This complete console example sends the repository's `account-opened` email template and prints
its id and current status. Set `NOTIFICATION_SERVICE_ADDRESS` to your service's base address first;
configure its email sender using the [operator guide](operator-guide.md) before expecting delivery.

```csharp
using DKNet.Notification.Client;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddNotificationClient(new Uri(
    Environment.GetEnvironmentVariable("NOTIFICATION_SERVICE_ADDRESS")
    ?? throw new InvalidOperationException("Set NOTIFICATION_SERVICE_ADDRESS.")));

await using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<INotificationClient>();
var request = new SendNotificationRequest("email", "account-opened", new Dictionary<string, string>
{
    ["to"] = "jane@example.com",
    ["customerName"] = "Jane",
    ["accountNumber"] = "0012345678"
});
var idempotencyKey = "onboard-0012345678";
var sent = await client.SendAsync(request, idempotencyKey);
var status = await client.GetStatusAsync(sent.NotificationId);
Console.WriteLine($"{sent.NotificationId}: {status.Status}");
```

`sent.NotificationId` identifies an accepted request, whether queued or skipped. It does not prove
delivery. A skipped request returns an id but immediately reads `failed`; a fast queued delivery can
already read `success` or `failed` by your first lookup.

## 🔄 How it works

![The caller invokes SendAsync through its Refit client; the API validates the call, replays a cached acceptance or queues a new notification, and the client returns the id or throws NotificationApiException; GetStatusAsync then reads the caller's current status.](diagrams/notification-client.sequence.svg)

The client runs inside the calling application's process. `ServiceCollectionExtensions` registers
the Refit implementation of `INotificationClient` through `IHttpClientFactory`; the caller's optional
handler runs on its outgoing requests. Delivery happens asynchronously in the service after acceptance.

## 🧩 Features

### Add your application's bearer token

Both routes need a token identifying the caller through `client_id`, `azp` or `appid`, with
`notifications.send` in `scp`, `scope` or `roles` when service authorization is enabled.
Use the **same caller identity** for send and status lookup. The address-only registration above
adds no bearer token, so an authenticated deployment needs the second overload.

Replace the Quick Start registration with these lines:

```csharp
services.AddTransient<BearerTokenHandler>();
services.AddNotificationClient(new Uri(
    Environment.GetEnvironmentVariable("NOTIFICATION_SERVICE_ADDRESS")
    ?? throw new InvalidOperationException("Set NOTIFICATION_SERVICE_ADDRESS.")), typeof(BearerTokenHandler));
```

For an executable example, append this handler after the console program's statements and supply
`NOTIFICATION_BEARER_TOKEN` through your application's secret store or environment:

```csharp
public sealed class BearerTokenHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = Environment.GetEnvironmentVariable("NOTIFICATION_BEARER_TOKEN")
            ?? throw new InvalidOperationException("Set NOTIFICATION_BEARER_TOKEN.");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(request, cancellationToken);
    }
}
```

In your application, replace the environment lookup with your existing token acquisition and
refresh mechanism. Feed credentials restore the
package; the bearer token authorizes calls to the running service.

### Choose the channel, template and parameters

The Quick Start calls `SendAsync` with `email`, the registered `account-opened` template and every
template parameter. Email needs one `to` address. For `teams`, use the configured `teamsDestination`
name, never a webhook URL, plus that template's required parameters.

Template ids match exactly; channels match without case. An unsupported or unconfigured channel,
a missing channel version, or an unknown Teams destination can be accepted as skipped, with status
`failed`. Validation and rendering refusals return no id. See [send a notification](features/send-notification.md)
for field bounds and the queue contract.

### Read the current outcome

Call `GetStatusAsync(sent.NotificationId)` as in the Quick Start; it returns `NotificationId`,
`IdempotencyKey` and the `NotificationStatus` enum. The wire values map as follows:

| Wire value | Client value | Meaning for the caller |
|---|---|---|
| `pending` | `NotificationStatus.Pending` | Queued, being attempted, or waiting for a service delivery retry; no final outcome yet. |
| `success` | `NotificationStatus.Success` | The provider accepted the message; this is not proof the recipient read it. |
| `failed` | `NotificationStatus.Failed` | Skipped, permanently refused by delivery, or delivery attempts exhausted. |

Transient delivery failures leave status `pending` while the service retries. Status retention
defaults to 24 hours from each write. An unknown, expired or another caller's id returns
`404 NOTIFICATION_NOT_FOUND`. Status writes are best effort: a missing record does not prove failure,
and a lost queue item can remain `pending` until expiry. The API returns no provider details or skip
reason. See [read notification status](features/notification-status.md) for the storage limits.

### Reuse the key when retrying

The `idempotencyKey` argument is the `Idempotency-Key` header, sent exactly as given. Supply
1–255 letters, digits, hyphens or underscores. Keep the same key and request for retries of one
logical notification; a fresh key can create a second notification.

The service scopes keys by caller, route and method. It replays the first accepted response,
including the same notification id, during the default 4-hour cache lifetime. Replaying does not
enqueue again or return a fresh delivery outcome: read status separately. Refusals are not cached;
a running or refused call can hold its key for 30 seconds. The client adds no retry policy of its own;
the calling application's handlers, including globally configured resilience handlers, can retry.

### Handle refusals by status and code

Non-success HTTP responses throw `NotificationApiException`. `StatusCode` is a `HttpStatusCode`;
`Errors` is an `IReadOnlyList<NotificationApiError>` carrying `Code`, `Field` and `Message`.
Branch on the status and code; messages can change, and the list can be empty.

Wrap the Quick Start's send call in this handling pattern:

```csharp
try
{
    var accepted = await client.SendAsync(request, idempotencyKey);
    Console.WriteLine(accepted.NotificationId);
}
catch (NotificationApiException ex)
{
    Console.WriteLine($"HTTP {(int)ex.StatusCode}");
    foreach (var error in ex.Errors)
    {
        Console.WriteLine($"{error.Code}: {error.Field} — {error.Message}");
    }
}
```

These are all named codes produced by the two notification routes:

| Route | HTTP status | Code | Caller action |
|---|---|---|---|
| Send | 400 | `INVALID_REQUEST` | Correct the body or field bounds. |
| Send | 400 | `TEMPLATE_NOT_FOUND` | Use a registered template id. |
| Send | 400 | `RECIPIENT_MISSING` | Supply `to` for email or `teamsDestination` for Teams. |
| Send | 400 | `RECIPIENT_INVALID` | Correct the single email address or destination name. |
| Send | 400 | `PARAMETER_MISSING` | Supply every token required by the template. |
| Send | 400 | `MESSAGE_TOO_LARGE` | Reduce the rendered Teams message to at most 28,672 bytes. |
| Send | 503 | `QUEUE_FULL` | Wait 30 seconds, then send again with the same idempotency key. |
| Status | 404 | `NOTIFICATION_NOT_FOUND` | Check the id, caller identity and retention; do not infer delivery failure. |

Other refusals need status-based handling and can have an empty error list:

| HTTP status | When |
|---|---|
| 400 | Missing or invalid idempotency header on send. |
| 409 | The same send key is still held by a running or refused call. |
| 401 / 403 | Missing or invalid caller identity or permission, on either route. |
| 413 | Send body exceeds 65,536 bytes. |
| 429 | Service rate limiter refuses either route. |
| 500 | Service dependency or unexpected failure, including backlog/publish or status-read failure. |
| 504 | Service request timeout expires on either route. |

The client also uses `NotificationApiException` for an unreadable success body, preserving its
HTTP status and an empty error list. Transport failures and timeouts reach the caller unchanged,
for example `HttpRequestException` or `TaskCanceledException`.

## ⚙️ Configuration reference

| Registration argument | Type | Default | Effect |
|---|---|---|---|
| `baseAddress` | `Uri` | Required | Service base address for the typed client. |
| `messageHandlerType` | `Type` | Omitted in the address-only overload | Chains the registered `DelegatingHandler` onto outgoing requests. |

Register the handler in the same service collection. A missing argument or a handler type that does
not derive from `DelegatingHandler` fails registration. Both client methods accept an optional
`CancellationToken`, defaulting to no caller cancellation.

## 🧱 Where it fits

The package is part of the caller, with its own contracts and no reference to the API projects.
The [as-built runtime diagram](runtime-architecture.md) shows that caller crossing the service edge
through plain HTTPS or the client package. Operators configure delivery providers in the service;
callers submit template parameters.

## ⚠️ Gotchas & limits

- Acceptance does not promise delivery. Keep the returned id and inspect status when the outcome matters.
- The client obtains no token and adds no timeout or retry policy. Coordinate application retries with
  the same idempotency key, including retries configured for all HTTP clients.
- Keep each key unique to one logical notification; replay expires and is not a permanent deduplication ledger.
- Polling is application-owned. The client neither polls automatically nor chooses a polling interval.

## 🔗 Related docs

- [Package README](../ApiEndpoints/DKNet.Notification.Client/README.md) — compact registration and call examples.
- [Send a notification](features/send-notification.md) — direct HTTP contract and parameter bounds.
- [Read notification status](features/notification-status.md) — caller isolation, retention and missing records.
- [Operator guide](operator-guide.md) — service authorization, templates and channel configuration.

## ❓ Open questions

| Question | Why it matters | Checked | Who can answer |
|---|---|---|---|
| Which release first publishes the client package? | Determines when the install commands can succeed. | Client project and release workflow; no release date is configured. | Release owner |
| What polling interval and overall wait should each caller use? | Controls load and the caller's response to a prolonged pending status. | Client interface, status settings and status feature page. | Calling application owner |
