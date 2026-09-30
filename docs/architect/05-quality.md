# 05 — Quality attributes

## Security

### Trust boundaries

| Boundary | Inside | Outside | Crossing rule |
|---|---|---|---|
| Service edge | The Notification API and its delivery worker | Callers | Every call except `/healthz` needs a valid Entra ID bearer token |
| Data store | Redis | The service | Password and TLS where the platform offers them; idempotency records only |
| Delivery targets | SMTP provider, Teams Workflows | The service | SMTP with STARTTLS or TLS and credentials; HTTPS only to webhook URLs from settings |

### Authentication

- Bearer tokens from Microsoft Entra ID, validated from its OpenID Connect metadata.
- The settings are the scaffold's `Authentication:Schemes:Bearer` section: `MetadataAddress`, `ValidIssuer`, `ValidAudiences`. DKNet.Accounts.Api uses the same section.
- Inbound claims are not remapped, as in DKNet.Accounts.Api.
- The default policy denies any endpoint that is not declared anonymous.

### Authorization

| Endpoint group | Rule |
|---|---|
| `POST /v1/notifications` | The token carries `notifications.send` in `scp`, `scope` or `roles` (ADR-0007) |
| `GET /healthz` | Anonymous; status only, no detail |

- Every endpoint declares its scope with the DKNet.AspCore.Extensions endpoint scope declaration, as DKNet.Accounts.Api does.
- A caller's identity is the first of its `client_id`, `azp` and `appid` claims, the order DKNet.Accounts.Api uses.
- Idempotency keys are scoped by that caller identity, through the `KeyScopeResolver` setting (ADR-0008). Two callers can use the same key without clashing.

### Content safety

- Email: every parameter value is HTML-encoded before it fills an HTML body. A value cannot inject markup or script.
- Email subject: values go in as text. CR and LF in the finished subject are replaced by spaces, so no header can be injected.
- Teams: values go into the Markdown body unchanged. A value can add Markdown formatting or a link. This is accepted because every caller is an authenticated backend service.
- Teams: the card is built by a JSON serializer, so no value can change the card's structure.
- Square brackets are literal text. Only `{{name}}` is a token (ADR-0004).
- The service sends only to webhook URLs from its own settings. A caller names a destination; it never sends a URL. This closes server-side request forgery.

### Personal data

- Personal data: email addresses, and any parameter value, such as a name or an account number.
- It flows: caller → API → memory → SMTP provider or Teams. It is never stored.
- Logs, metrics and traces never hold a parameter value, a recipient or a rendered body.
- Idempotency records hold only the 202 body: the `notificationId`.

### Secrets

| Secret | Where it lives |
|---|---|
| SMTP password | Environment variable or Azure App Configuration; user secrets for local runs |
| Teams webhook URLs | Environment variable or Azure App Configuration; user secrets for local runs |
| Redis connection string | `ConnectionStrings:Redis`, from the same sources |

No secret is in `appsettings.json` or the image. This is the configuration order DKNet.Accounts.Api documents.

## Observability

### Health

- `GET /healthz` reports liveness only.
- It does not check SMTP or Teams. An outage there must not restart the service; it is retried and logged instead.
- It does not check a database, because there is none (ADR-0002).

### Logs

One structured entry per state change. Every entry carries `notificationId`, template id, channel, caller id and trace id.

| Event | Level | Extra fields |
|---|---|---|
| NotificationRejected | Information | Error code |
| NotificationSkipped | Warning | Reason: `ChannelNotSupported`, `ChannelNotConfigured`, `NoTemplateVersion`, `TeamsDestinationNotConfigured` |
| NotificationQueued | Information | Queue length |
| NotificationAttemptFailed | Warning | Attempt number, failure kind (transient or permanent), provider status code |
| NotificationDelivered | Information | Attempt number, duration |
| NotificationFailed | Error | Attempt count, last provider status code |

### Metrics

| Metric | Kind | Tags |
|---|---|---|
| `notifications.accepted` | Counter | channel, outcome (`queued`, `skipped`) |
| `notifications.rejected` | Counter | error code |
| `notifications.delivered` | Counter | channel |
| `notifications.failed` | Counter | channel |
| `notifications.queue.length` | Gauge | — |
| `notifications.delivery.duration` | Histogram | channel |

Metrics and traces go out through the scaffold's OpenTelemetry wiring, behind its `EnableOpenTelemetry` flag.

### Correlation

- The OpenTelemetry trace id is the correlation id, as in DKNet.Accounts.Api.
- Error bodies carry `traceId`.
- The delivery worker links its activity to the accepting request's trace. One trace covers acceptance and delivery.
- `notificationId` joins every log entry of one notification.

## Performance and scale

| Figure | Value | Source |
|---|---|---|
| Expected calls per second | Unknown | Not given in the brief. Open question on DRK-1881 |
| Accept latency | Unknown target | Open question. Accepting does no network call except Redis |
| Teams webhook rate | Throttled above 4 requests per second | Microsoft Learn, "Create an Incoming Webhook" |
| Teams message size | 28 KB maximum | Microsoft Learn, "Create an Incoming Webhook" |
| Queue capacity | 1,000 notifications per replica | This design (DeliverySettings) |

- The delivery worker sends one notification at a time. This keeps Teams under its rate limit. Raise it only when a measured volume needs it.
- Replicas scale acceptance. Each replica has its own queue and worker. Redis keeps idempotency shared.

## Packaging and deployment

- **Container image:** multi-arch (`linux-x64`, `linux-arm64`), built with the .NET SDK container publish on an `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` base, non-root. Published to `ghcr.io/baoduy/dknet.notification-api`, like DKNet.Accounts.Api's `ghcr.io/baoduy/dknet.accounts-api`.
- **Version:** computed by the publish pipeline from tags. Never hand-edited.
- **CI:** build and test on every push and pull request to `dev`; image publish on push to `main`. The same two workflows DKNet.Accounts.Api runs.
- **Helm chart:** one chart for the API, like DKNet.Accounts.Api's `helm/dknet-accounts` (slice 5). It sets the channel settings, the destinations and the secret references.
- **Local run:** the Aspire AppHost starts Redis, a Mailpit SMTP catcher and the API.
- **No NuGet package:** callers use plain HTTP. A typed client package is out of scope for version 1.

## Testing approach

| Behaviour | Test kind | Real infrastructure | Faked |
|---|---|---|---|
| Evaluation order, every 400, 409 and 503 case | Integration, through HTTP | Redis (Testcontainers) | Entra ID: a test authentication handler, as DKNet.Accounts.Api does |
| Skip rule: all 4 reasons answer 202 and log a warning | Integration | Redis | Entra ID |
| Idempotent replay: same key gives the same `notificationId` and one send | Integration | Redis, Mailpit | Entra ID |
| Two callers, same key: each call is processed on its own, and each caller gets its own `notificationId` | Integration | Redis, Mailpit | Entra ID: two test identities with different `client_id` values |
| Same caller, same key, after a token refresh: the first 202 is replayed and nothing is sent twice | Integration | Redis, Mailpit | Entra ID: two tokens with the same `client_id` |
| Email delivery, HTML encoding, subject clean-up | Integration | Mailpit (Testcontainers); its API shows the received mail | Entra ID |
| SMTP retry and give-up | Integration | Mailpit stopped, then started | Entra ID |
| Teams card shape, 429 and 404 handling | Integration | — | Teams webhook: a local HTTP stub that records posts and answers 2xx, 429 or 404 |
| Template catalogue start-up checks | Unit | — | File system: a temporary `Templates` folder |
| No personal data in logs | Integration | Redis, Mailpit | Log sink captures entries; the test searches them for the recipient and each value |

## Runtime architecture

![A backend caller gets an Entra ID token and posts across the service edge to the Notification API inside the per-replica container; the API checks the idempotency record in Redis, which sits outside the container behind its own data-store boundary and is shared by all replicas, renders from the in-image template catalogue, queues the message and answers 202; the delivery worker sends it across the delivery-target boundary to the SMTP provider or a Teams Workflows webhook.](diagrams/runtime.svg)

The first docs ticket after the scaffold draws the code-derived diagram at `docs/diagrams/`. It reports any difference from this one as a design question.
