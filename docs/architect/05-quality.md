# 05 — Quality attributes

## Security

### Trust boundaries

| Boundary | Inside | Outside | Crossing rule |
|---|---|---|---|
| Service edge | The Notification API and its delivery consumer | Callers | Every call except `/healthz` needs a valid Entra ID bearer token |
| Data store | Redis | The service | Password and TLS where the platform offers them. It holds idempotency records, the delivery queue and status records. The queue holds personal data until delivery |
| Delivery targets | SMTP provider, Teams Workflows | The service | SMTP with STARTTLS or TLS and credentials; HTTPS only to webhook URLs from settings |
| Microsoft 365 tenant | Microsoft Graph and the sending mailbox | The service, as the mail-sender app | HTTPS with an Entra ID token for `https://graph.microsoft.com/.default`, issued to the mail-sender app; `Mail.Send` scoped to the one sending mailbox (ADR-0010) |

### Authentication

- Bearer tokens from Microsoft Entra ID, validated from its OpenID Connect metadata.
- The settings are the scaffold's `Authentication:Schemes:Bearer` section: `MetadataAddress`, `ValidIssuer`, `ValidAudiences`. DKNet.Accounts.Api uses the same section.
- Inbound claims are not remapped, as in DKNet.Accounts.Api.
- The default policy denies any endpoint that is not declared anonymous.

### Authorization

| Endpoint group | Rule |
|---|---|
| `POST /v1/notifications` | The token carries `notifications.send` in `scp`, `scope` or `roles` (ADR-0007) |
| `GET /v1/notifications/{notificationId}` | The same permission. The status is scoped to the caller: another caller's id answers 404 (ADR-0012) |
| `GET /healthz` | Anonymous; status only, no detail |

- Every endpoint declares its scope with the DKNet.AspCore.Extensions endpoint scope declaration, as DKNet.Accounts.Api does.
- A caller's identity is the first of its `client_id`, `azp` and `appid` claims, the order DKNet.Accounts.Api uses.
- Idempotency keys are scoped by that caller identity, through the `KeyScopeResolver` setting (ADR-0008). Two callers can use the same key without clashing.

### Graph sign-in

- The Graph sender signs in as the mail-sender app, its own Entra app registration. It never uses the API's registration (ADR-0010).
- `Credential` = `WorkloadIdentity`: the app trusts the service's Kubernetes service account through a federated identity credential. No secret exists.
- `Credential` = `ClientSecret`: the app proves itself with a client secret. Use it only on a host without workload identity, or for a manual check.

### Graph mailbox scope — required setup step

The `Mail.Send` application permission lets an app send as any mailbox in the tenant. Every Graph deployment limits it to the one sending mailbox before it turns the Graph sender on. The operator guide (slice 6) carries these steps.

1. Register the mail-sender app in Entra ID. Do not reuse the API's registration.
2. Give it its credential: a federated identity credential with issuer = the cluster's OIDC issuer, subject = `system:serviceaccount:<namespace>:<service account>`, audience = `api://AzureADTokenExchange`. Or, off Kubernetes, a client secret.
3. Do **not** grant or consent `Mail.Send` for the app in Entra ID. An Entra grant is tenant-wide, and an Exchange scope cannot narrow it: the two grants add up.
4. In Exchange Online, as an Exchange administrator:
   - add the app's service principal with `New-ServicePrincipal`;
   - create a management scope with `New-ManagementScope` whose recipient filter matches only the sending mailbox;
   - assign the role with `New-ManagementRoleAssignment -Role "Application Mail.Send" -App <app> -CustomResourceScope <scope>`.
5. Check it with `Test-ServicePrincipalAuthorization -Identity <app> -Resource <mailbox>`. It must show `InScope` true for the sending mailbox and false for any other mailbox.
6. Allow 30 minutes to 2 hours before the first send. Exchange caches app permissions for that long, so an early 403 is expected.

A tenant that already uses application access policies may use one instead of steps 3 and 4. It grants `Mail.Send` in Entra ID, then runs `New-ApplicationAccessPolicy -AccessRight RestrictAccess` against a mail-enabled security group that holds only the sending mailbox. A shared mailbox cannot be the policy's target directly. Microsoft says not to create new application access policies, so a new tenant uses RBAC for Applications.

### Content safety

- Email: every parameter value is HTML-encoded before it fills an HTML body. A value cannot inject markup or script.
- Email subject: values go in as text. CR and LF in the finished subject are replaced by spaces, so no header can be injected.
- Teams: values go into the Markdown body unchanged. A value can add Markdown formatting or a link. This is accepted because every caller is an authenticated backend service.
- Teams: the card is built by a JSON serializer, so no value can change the card's structure.
- Square brackets are literal text. Only `{{name}}` is a token (ADR-0004).
- The service sends only to webhook URLs from its own settings. A caller names a destination; it never sends a URL. This closes server-side request forgery.

### Personal data

- Personal data: email addresses, and any parameter value, such as a name or an account number.
- It flows: caller → API → the Redis delivery queue → delivery consumer → SMTP provider, Microsoft Graph or Teams.
- The queued message holds the recipient and the rendered body as they are, not encrypted. It stays in Redis until the notification is delivered or fails, then it is gone. Redis access control and TLS protect it, as they protect idempotency records (ADR-0013). Anyone who can read the Redis data can read the waiting messages.
- With the Graph sender, the sending mailbox keeps a copy of each email in Sent Items, under the tenant's retention (ADR-0009).
- Logs, metrics and traces never hold a parameter value, a recipient or a rendered body.
- Idempotency records hold only the 200 body: the `notificationId`.
- Status records hold the `notificationId`, the `Idempotency-Key` and the status. They hold no recipient and no body (ADR-0012).

### Secrets

| Secret | Where it lives |
|---|---|
| SMTP password | Environment variable or Azure App Configuration; user secrets for local runs |
| Graph client secret (only with `Credential` = `ClientSecret`) | Environment variable or Azure App Configuration; user secrets for local runs |
| Teams webhook URLs | Environment variable or Azure App Configuration; user secrets for local runs |
| Redis connection string | `ConnectionStrings:Redis`, from the same sources |

No secret is in `appsettings.json` or the image. This is the configuration order DKNet.Accounts.Api documents.

With `Credential` = `WorkloadIdentity`, the Graph sender holds no secret. Kubernetes projects the service account token into the pod, and Entra ID exchanges it for an access token. No log entry ever holds a token or a secret.

## Observability

### Health

- `GET /healthz` reports liveness only.
- It does not check SMTP, Microsoft Graph, the Entra ID token endpoint or Teams. An outage there must not restart the service; it is retried and logged instead.
- It does not check a database, because there is none (ADR-0002).

### Logs

One structured entry per state change. Every notification entry carries `notificationId`, template id, channel, caller id and trace id. The two start-up entries carry none of them.

| Event | Level | Extra fields |
|---|---|---|
| NotificationRejected | Information | Error code |
| NotificationSkipped | Warning | Reason: `ChannelNotSupported`, `ChannelNotConfigured`, `NoTemplateVersion`, `TeamsDestinationNotConfigured` |
| NotificationQueued | Information | Queue length: the service-wide list length, counting this notification |
| NotificationAttemptFailed | Warning | Attempt number, failure kind (transient or permanent), provider status code |
| NotificationDelivered | Information | Attempt number, duration |
| NotificationFailed | Error | Attempt count, last provider status code |
| NotificationStatusWriteFailed | Warning | Event id 2007. The status that could not be written. Carries `notificationId` and caller id only: no error text, because it may hold a connection string |
| NotificationRequeued | Debug | Event id 2008. Why the message went back to the queue (`not-due`, `retry` or `stopping`) and the attempts made. Carries `notificationId` and trace id only |
| EmailSenderStarted | Information | Once at start-up: the selected sender (`Smtp` or `Graph`). No `notificationId` |
| EmailSenderNotConfigured | Warning | Once at start-up, only when email `Enabled` is `true`: the names of the missing settings, or `Sender` when its value is unknown. Never a setting's value. No `notificationId` |

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

The gauge `notifications.queue.length` reads `LLEN` on the Redis delivery list each time it is scraped. It is the backlog of the whole service, the same value on every replica, not the count one replica holds. It reads 0 on the memory fallback of local runs. A scrape that runs after the bus is disposed at shutdown can fail.

### Correlation

- The OpenTelemetry trace id is the correlation id, as in DKNet.Accounts.Api.
- Error bodies carry `traceId`.
- The delivery consumer links its activity to the accepting request's trace, which the queued message carries. One trace covers acceptance and delivery.
- `notificationId` joins every log entry of one notification.

## Delivery guarantees

Delivery is best effort, but a notification that waits survives a restart (ADR-0013).

| Event | What happens to a notification |
|---|---|
| A deploy or a stop while a notification waits in the queue, or waits for a retry | Nothing is lost. The message is in Redis, and any replica takes it when its time comes |
| A stop during a delivery attempt | The message is put back with `AttemptsMade` unchanged. The cut-off attempt is not counted, so a run of deploys cannot use up the 3 attempts |
| A hard crash of a replica | At most 1 message is lost: the one that replica had taken. Its status stays `pending` until the record expires |
| Redis loses its data | The queue is lost, and so are the status records. Callers read 404 for them |
| A message that cannot be deserialized | SlimMessageBus drops it before our code runs. Its status stays `pending` until it expires. The compatibility rule in ADR-0013 prevents this across releases |
| An ambiguous timeout, or a stop that cuts an attempt after the provider already took the message | A duplicate is possible: the retry sends the message again |
| A failed status write | The call or the delivery goes on. The failure is logged, and the status can lag or stay `pending` until it expires (ADR-0012) |
| A failed publish back to the queue, in the consumer | The notification ends Failed: it is logged, counted and, if the write works, shown as `failed`. No exception leaves the consumer, because the provider would drop the message |

- Delivery is at most once per pop of the Redis list. Exactly-once delivery is not offered.
- Azure Service Bus is the upgrade path if at-least-once delivery is ever needed (ADR-0013).

## Performance and scale

| Figure | Value | Source |
|---|---|---|
| Expected calls per second | Unknown | Not given in the brief. Open question on DRK-1881 |
| Accept latency | Unknown target | Open question. Accepting does no network call except Redis |
| Teams webhook rate | Throttled above 4 requests per second | Microsoft Learn, "Create an Incoming Webhook" |
| Teams message size | 28 KB maximum | Microsoft Learn, "Create an Incoming Webhook" |
| Queue capacity | 1,000 notifications for the whole service, counted on the Redis list | This design (DeliverySettings, ADR-0013) |
| Status retention | 24 hours after the last write; 1 to 168 | This design (NotificationStatusSettings, ADR-0012) |
| Graph sending mailbox | 30 messages per minute; 10,000 recipients per day | Microsoft Learn, "Exchange Online limits" (message rate limit, recipient rate limit) |

- Each replica's delivery consumer sends one notification at a time (`Instances(1)`). This keeps Teams under its rate limit. Raise it only when a measured volume needs it.
- Replicas scale acceptance and delivery. They share one queue in Redis, and each replica has one consumer. Redis also keeps idempotency and status shared.
- A message that is not yet due goes back to the tail of the list at once, and the consumer pauses at most 1 second. A list made only of such messages costs about one Redis round trip per second per replica.
- The capacity check reads `LLEN` before it publishes. It is approximate: replicas publish at the same time, so the list can pass the limit by a few messages.
- With the Graph sender, every replica sends from the same mailbox. Its limits are shared by all replicas.

## Packaging and deployment

- **Container image:** multi-arch (`linux-x64`, `linux-arm64`), built with the .NET SDK container publish on an `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` base, non-root. Published to `ghcr.io/baoduy/dknet.notification-api`, like DKNet.Accounts.Api's `ghcr.io/baoduy/dknet.accounts-api`.
- **Version:** computed by the publish pipeline from tags. Never hand-edited.
- **CI:** build and test on every push and pull request to `dev`; image publish on push to `main`. The same two workflows DKNet.Accounts.Api runs.
- **Helm chart:** one chart for the API, like DKNet.Accounts.Api's `helm/dknet-accounts` (slice 6). It sets the channel settings, the email sender, the destinations and the secret references.
- **Workload identity:** for `Credential` = `WorkloadIdentity`, the chart's service account carries the `azure.workload.identity/client-id` annotation with the mail-sender app's client id. DKNet.Accounts.Api's chart sets the same annotation for its own identity. The pod template also carries the label `azure.workload.identity/use: "true"`; without it the workload identity webhook injects no token. DKNet.Accounts.Api's chart sets no such label.
- **Local run:** the Aspire AppHost starts Redis, a Mailpit SMTP catcher and the API, with `Sender` = `Smtp`. Graph has no local stand-in in the AppHost.
- **No NuGet package:** callers use plain HTTP. A typed client package is out of scope for version 1.

## Testing approach

| Behaviour | Test kind | Real infrastructure | Faked |
|---|---|---|---|
| Evaluation order, every 400, 409 and 503 case | Integration, through HTTP | Redis (Testcontainers) | Entra ID: a test authentication handler, as DKNet.Accounts.Api does |
| Skip rule: all 4 reasons answer 200 and log a warning | Integration | Redis | Entra ID |
| Idempotent replay: same key gives the same `notificationId` and one send | Integration | Redis, Mailpit | Entra ID |
| Two callers, same key: each call is processed on its own, and each caller gets its own `notificationId` | Integration | Redis, Mailpit | Entra ID: two test identities with different `client_id` values |
| Same caller, same key, after a token refresh: the first 200 is replayed and nothing is sent twice | Integration | Redis, Mailpit | Entra ID: two tokens with the same `client_id` |
| Email delivery, HTML encoding, subject clean-up | Integration | Mailpit (Testcontainers); its API shows the received mail | Entra ID |
| SMTP retry and give-up | Integration | Mailpit stopped, then started | Entra ID |
| Teams card shape, 429 and 404 handling | Integration | — | Teams webhook: a local HTTP stub that records posts and answers 2xx, 429 or 404 |
| Graph request shape: mailbox in the path, one `to`, HTML body, subject, no CC, BCC, attachment or `from`, no `saveToSentItems` | Integration | Redis | Entra ID inbound: test handler. Graph: a local HTTP stub that records each request and answers 202. Graph token: a fixed test token |
| Graph retry: 429 with `Retry-After`, 503 and a timeout are retried; a third transient answer ends Failed | Integration | Redis | Graph stub answers in a set order |
| Graph permanent answers: 400, 403 and 404 end Failed after 1 attempt | Integration | Redis | Graph stub |
| Graph token step: token 408, 429 (with `Retry-After`), 5xx and a timeout are retried; another 4xx, such as `invalid_client`, ends Failed after 1 attempt; the credential makes 1 token request per attempt, with no retry of its own | Integration | Redis | Entra ID token endpoint: a local HTTP stub that answers in a set order and counts requests |
| Graph token reuse: 2 notifications in a row ask for 1 token | Integration | Redis | Graph token source counts its calls |
| One sender per deployment: with `Sender` = `Graph`, Mailpit receives nothing; with `Sender` = `Smtp`, the Graph stub receives nothing | Integration | Redis, Mailpit | Graph stub |
| Missing sender setting: with `Sender` = `Graph` and no `Mailbox`, the host starts, logs `EmailSenderNotConfigured` naming `Mailbox`, and email calls answer 200 and end Skipped | Integration | Redis | Entra ID |
| Status: a delivered email reads `success` with its key; an email that fails 3 times reads `pending` then `failed`; a skipped call reads `failed` at once; another caller's id answers 404 `NOTIFICATION_NOT_FOUND` | Integration | Redis (one container per feature), Mailpit | Entra ID |
| Restart: a notification waiting for its retry is delivered by a new host started on the same Redis, on attempt 2 | Integration | Redis, Mailpit stopped then started | Entra ID |
| Full queue: a list at `QueueCapacity` answers 503 `QUEUE_FULL` | Integration | Redis, Mailpit stopped | Entra ID |
| Delivery consumer: not due is put back unchanged; success; transient puts back `AttemptsMade` + 1 with the configured or `Retry-After` wait; a 3rd transient, a permanent failure, an unexpected exception and a missing sender end `failed`; a stop mid-attempt puts it back unchanged; a failed publish back ends `failed` | Unit | — | Senders, bus and clock (`FakeTimeProvider`) |
| Status store: caller-scoped key, expiry from settings, a failed write does not throw | Unit | — | `IDistributedCache` |
| Status route: `POST` answers 200; `GET` answers 200 for the caller's own id and 404 for an unknown, another caller's or non-GUID id | Integration, through HTTP | — (in-memory store) | Entra ID |
| Template catalogue start-up checks | Unit | — | File system: a temporary `Templates` folder |
| No personal data or secret in logs | Integration | Redis, Mailpit, Graph stub | Log sink captures entries; the test searches them for the recipient, each value, the Graph token and the client secret |

- No test reaches a live Microsoft 365 tenant, and none runs in CI.
- The test host points the Graph sender at the Graph stub. It replaces the token source with a fixed test token, or, for the token-step row, points a `ClientSecret` credential at the token endpoint stub. Production has no setting for any of these.
- A real tenant is checked by hand, with the steps in the operator guide (slice 6): `Test-ServicePrincipalAuthorization`, then one test notification.

## Runtime architecture

![A backend caller gets an Entra ID token and posts across the service edge to the Notification API inside the per-replica container; the API checks the idempotency record in Redis, which sits outside the container and is shared by all replicas, renders, writes the pending status, publishes the message to the delivery list in Redis and answers 200, and answers the caller's status lookup from Redis; the delivery consumer takes messages from that list, puts a message back to wait for a retry, writes the final status, and hands email to the one active sender — the SMTP sender to the SMTP provider, or the Graph sender, which gets an Entra ID token as the mail-sender app and posts to Microsoft Graph — and Teams messages to a Teams Workflows webhook.](diagrams/runtime.svg)

The first docs ticket after the scaffold draws the code-derived diagram at `docs/diagrams/`. It reports any difference from this one as a design question.
