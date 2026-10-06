# Operator guide

Audience: operators configuring DKNet.Notification.Api on Kubernetes. This guide is the configuration reference and channel setup for SMTP, Microsoft Graph and Microsoft Teams. For installation, rollout checks and rollback, use the [deployment guide](deployment.md).

Configuration below describes repository code at commit `362962797612943392d6ecb73686eed9c742a574`.

## 1. Prerequisites

Before you install the chart, have ready:

- An AKS cluster with its OIDC issuer and workload identity enabled. Both federated credentials below (Key Vault identity, mail-sender app) use that issuer.
- The Azure Key Vault Secrets Store CSI driver installed on the cluster.
- An existing Redis instance reachable from the cluster. The service refuses to start in production without a connection string to it — see [§2 Redis](#redis).
- An Azure Key Vault holding the service's secrets (§2 lists them).
- A user-assigned managed identity with read permission on that vault's secrets, with workload identity federation to the cluster's service account. If the vault was created with `--enable-rbac-authorization`, grant the `Key Vault Secrets User` role: `az role assignment create --role "Key Vault Secrets User" --assignee <identity-client-id> --scope <vault-resource-id>`. Otherwise, grant a vault access policy with `--secret-permissions get`.
- The API's own Entra ID app registration, for validating caller bearer tokens (`Authentication:Schemes:Bearer`). This is a separate registration from the mail-sender app in §3 — never reuse one for the other.
- An existing Gateway API gateway, only if you plan to expose the API outside the cluster (§4 `api.httpRoute`). The chart creates no Gateway resource.

## 2. Configuration reference

Every setting below is an ASP.NET Core configuration key. The chart writes each as a Kubernetes-style double-underscore environment variable, e.g. the key `Notifications:Email:Smtp:Host` becomes `Notifications__Email__Smtp__Host`.

### Channels

| Key | Env var | Default | Secret? | Source |
|---|---|---|---|---|
| `Notifications:Email:Enabled` | `Notifications__Email__Enabled` | `false` | No | Plain settings |
| `Notifications:Email:Sender` | `Notifications__Email__Sender` | `Smtp` | No | Plain settings. `Smtp` or `Graph`, matched without case |
| `Notifications:Email:TimeoutSeconds` | `Notifications__Email__TimeoutSeconds` | `30` | No | Plain settings. 1–120 |
| `Notifications:Teams:Enabled` | `Notifications__Teams__Enabled` | `false` | No | Plain settings |
| `Notifications:Teams:TimeoutSeconds` | `Notifications__Teams__TimeoutSeconds` | `30` | No | Plain settings. 1–120 |
| `Notifications:Delivery:QueueCapacity` | `Notifications__Delivery__QueueCapacity` | `1000` | No | Plain settings. 1–100,000, **for the whole service**, not per replica (see [Redis](#redis)) |
| `Notifications:Delivery:MaxAttempts` | `Notifications__Delivery__MaxAttempts` | `3` | No | Plain settings. 1–3 |
| `Notifications:Delivery:RetryDelaysSeconds` | `Notifications__Delivery__RetryDelaysSeconds__0/1` | `[5, 30]` | No | Plain settings. Exactly 2 values, each 1–300 seconds |
| `Notifications:Status:RetentionHours` | `Notifications__Status__RetentionHours` | `24` | No | Plain settings. 1–168. How long a caller can read the status of a notification, counted from its last write |

**A bad `Delivery` or `Status` value stops the service from starting.** A bad `Email` or `Teams` value does not — it only leaves that channel unable to send (see "What a bad value does" below).

### Email senders

Exactly one sender is active per deployment, chosen by `Notifications:Email:Sender`.

**SMTP** (`Notifications:Email:Smtp:*`, read only when `Sender` is `Smtp`):

| Key | Env var | Default | Secret? |
|---|---|---|---|
| `Host` | `Notifications__Email__Smtp__Host` | *(empty — required, ≤255 chars)* | No |
| `Port` | `Notifications__Email__Smtp__Port` | `587` | No. 1–65,535 |
| `Security` | `Notifications__Email__Smtp__Security` | `StartTls` | No. `StartTls` or `Tls` — no plain-text mode exists |
| `UserName` | `Notifications__Email__Smtp__UserName` | *(empty — no sign-in, ≤256 chars)* | No |
| `Password` | `Notifications__Email__Smtp__Password` | *(empty, ≤512 chars)* | **Yes — Key Vault only, never a settings file** |
| `FromAddress` | `Notifications__Email__Smtp__FromAddress` | *(empty — required `local@domain`, ≤254 chars)* | No |
| `FromName` | `Notifications__Email__Smtp__FromName` | `DKNet Notification` (≤100 chars) | No |

**Microsoft Graph** (`Notifications:Email:Graph:*`, read only when `Sender` is `Graph` — see §3 before turning this on):

| Key | Env var | Default | Secret? |
|---|---|---|---|
| `TenantId` | `Notifications__Email__Graph__TenantId` | *(empty — required GUID)* | No |
| `ClientId` | `Notifications__Email__Graph__ClientId` | *(empty — required GUID)* | No |
| `Credential` | `Notifications__Email__Graph__Credential` | `WorkloadIdentity` | No. `WorkloadIdentity` or `ClientSecret` |
| `ClientSecret` | `Notifications__Email__Graph__ClientSecret` | *(empty, ≤512 chars)* | **Yes, required and only read with `Credential=ClientSecret` — Key Vault only** |
| `Mailbox` | `Notifications__Email__Graph__Mailbox` | *(empty — required `local@domain`, ≤254 chars)* | No. The one sending mailbox |

### Teams destinations

- Up to 100 destinations, each a name → webhook URL pair under `Notifications:Teams:Destinations:<name>:WebhookUrl`. **More than 100 destinations turns Teams off entirely** — not just the extra ones past 100 — the same as any other bad Teams setting (see "What a bad value does" below).
- A webhook URL must be an absolute `https://` URL of at most 2,048 characters, or the destination is treated as not set.
- **Destination names are matched with case**, and a caller may only ever name a destination of 1–64 lowercase letters, digits or `-` — a caller can never reach a destination named with an uppercase letter, so always name destinations in lowercase.
- The destination name is the `<name>` segment of the environment variable key the chart writes — the segment between `Destinations` and `WebhookUrl` in `Notifications__Teams__Destinations__<name>__WebhookUrl` — **not** the Key Vault secret name. Key Vault secret names allow only letters, digits and `-` (no `_`), so the chart's convention is `__` → `--`, lowercased, on the Key Vault side, while the env var key keeps the real double-underscore form. Example: the vault secret `notifications--teams--destinations--ops-alerts--webhookurl` becomes the env var `Notifications__Teams__Destinations__ops-alerts__WebhookUrl`, whose `<name>` segment — and so the destination name a caller must send — is `ops-alerts`.
- **Never put a webhook URL in the chart's plain `configMap` settings.** Anyone who can read the ConfigMap can then post to that Teams channel. A destination must be created through its Key Vault secret alone (`secretProvider.objects` + `secretProvider.secretObjects`), never through `api.configMap`. The chart's own CI template run exercises an empty `WebhookUrl` string directly in `configMap` to prove the chart renders — that CI convenience is not a deployment pattern to copy.

### Templates

- `Notifications:Templates` is a list of template registrations; each names an id, a description and one version per channel (`email` → HTML, `teams` → Markdown), and each version's file is loaded from the `Templates` folder inside the container image at start-up.
- The container's root filesystem is read-only in the chart (`securityContext.readOnlyRootFilesystem: true`), so **a new template file can only ship in a new image** — you cannot add or edit a template file at deploy time through settings.
- The email subject and the Teams title live in the template file, so **changing them needs a new image** too:
  - Email: the HTML `<title>` in `<head>` is the subject (entities such as `&amp;` are decoded). It stays in the sent mail; a browser preview and mail clients do not show it on the page.
  - Teams: the YAML front matter `title` key. Quote a value that starts with `{{`. The front matter is not part of the sent body.
  ```markdown
  ---
  title: "Account {{accountNumber}} opened"
  ---
  **{{customerName}}** has completed onboarding.
  ```
- You *can* change a shipped template's description through settings, because `Notifications:Templates` is bound as an indexed list: an environment variable such as `Notifications__Templates__0__Description` overwrites that field of the shipped `account-opened` entry at index 0. A leftover `Subject` or `Title` setting is ignored without an error. Adding a new index (e.g. index `1`) adds a template registration, but its file must already exist in the image.
  > ⚠️ **Index `0` is the shipped `account-opened` template.** An environment variable at `Notifications__Templates__0__...` silently overwrites that entry's field instead of adding a new one — there is no separate "add" key. Use an index the release's own catalogue does not already use (check the image's `appsettings.json` first) for anything you mean to add rather than change.
- A broken catalogue — a bad id, a missing file, two versions for one channel, an email file with no `<title>`, a subject over 500 characters — **stops the service from starting**.

### What a bad value does

| Setting group | A bad value does this |
|---|---|
| `Notifications:Delivery:*` / `Notifications:Status:*` | Stops the service from starting |
| `Notifications:Templates` (the catalogue) | Stops the service from starting |
| `Notifications:Email:*` / `Notifications:Email:Smtp:*` / `Notifications:Email:Graph:*` | The service starts; email is left "not configured" and every email call is skipped (logged once at start-up as a warning, naming the bad settings — never their values) |
| `Notifications:Teams:*` | The service starts; Teams is left "not configured" and every Teams call is skipped with reason `ChannelNotConfigured` — **no log entry is written at start-up for this one**, unlike email (see the [deployment checks](deployment.md#-verify) for the per-call skip reason to look for) |

### Redis

- `ConnectionStrings:Redis` holds the connection string of the one Redis the service uses. It carries three things: request idempotency records, the delivery queue and the notification status records.
- **In `Production` (the chart's `ASPNETCORE_ENVIRONMENT`), a missing `ConnectionStrings:Redis` stops the service from starting.** The in-memory fallback exists only for local `Development` and `Testing` runs — it is not a production degrade-to-single-replica path.
- More than one replica needs Redis for a second reason even where it did start: without it, idempotency, the status records and the delivery queue would each keep separate in-memory state per replica, so a retried call could be treated as new on a different replica. The chart lists the Redis secret in its default `secretProvider.objects`/`secretObjects`, so it is required, not opt-in.

**Delivery and status now live in Redis** (ADR-0013, ADR-0012):

- **The delivery queue** is the Redis list `notification-delivery`. Every notification that waits for delivery, or waits for a retry, is one message in it. All replicas share the list, and each replica runs one consumer. A deploy or a restart loses no waiting notification; a replica that stops during an attempt puts its message back. A stop can lose at most the one message the replica was taking from the list at that instant, and a hard crash of a replica can lose the one message it had taken; the status of either stays `pending` until it expires. Redis data loss loses the whole queue.
- **Personal data sits in Redis until delivery.** A queued message holds the recipient and the rendered body, not encrypted. Restrict who can read this Redis, and keep TLS and a password on it, as for the idempotency records. The message is gone once the notification is delivered or has failed.
- **Status records** are the keys `DKNet.Notification.Apistatus:{callerId}:{notificationId}`: the cache instance name `DKNet.Notification.Api` comes first, with no separator. They hold the notification id, the `Idempotency-Key` and the status, no personal data. They expire `Notifications:Status:RetentionHours` after their last write: 24 hours by default.
- **`Notifications:Delivery:QueueCapacity` is now for the whole service.** The API counts the Redis list, and answers `503 QUEUE_FULL` when it holds that many notifications. Before this release it was a limit per replica. A value you sized for one replica now has to cover all of them, so **raise it when you run more replicas**. The count is approximate, so the list can pass the limit by a few messages. Local runs with no Redis have no limit. A message that waits for its retry stays in the list and counts toward the limit. Each such message at the head of the list also holds up the messages behind it by up to a second, so during a Teams outage email delivery slows down and the list fills faster.
- **The gauge `notifications.queue.length` changed meaning.** It reads the length of the Redis list, so every replica reports the same service-wide backlog, not the count that replica holds. **A dashboard or an alert built on the per-replica value must be rebuilt.** Do not add the replicas up: each one reports the same number, so a sum counts the backlog once per replica. Read one value instead, for example the maximum. A scrape that runs after the bus is disposed at shutdown can fail; that is expected during a stop.
- **A Redis outage stops delivery** and answers every call with `500`, as before. SlimMessageBus can log a `TaskCanceledException` when the service stops while Redis is down.
- A flush of this Redis removes the waiting notifications, the status records and the idempotency records together.

### The other settings the chart sets or needs

| Key | Chart default | Notes |
|---|---|---|
| `Authentication:Schemes:Bearer:MetadataAddress` / `ValidIssuer` / `ValidAudiences:0` | From Key Vault | The API's own Entra ID app registration (§1); required for callers to authenticate |
| `FeatureManagement:RequireAuthorization` | `"true"` | Must never be `"false"` in a real deployment — it is the only thing requiring a bearer token on every route |
| `FeatureManagement:EnableHttps` | `"false"` | TLS ends at the gateway in front of this Service, not in the container |
| `Security:TrustedProxies:0` | Commented out | Uncomment together with `operator.trustedProxy` only once you know the gateway's real peer address — an **empty** value here fails `IPAddress.Parse` and crashes the service at start-up |

## 3. Microsoft Graph setup (required before you turn the Graph sender on)

Skip this section if you use the SMTP sender.

> ⚠️ **`Mail.Send` lets an app send as any mailbox in the tenant until you scope it.** Steps 3–4 below limit it to the one sending mailbox. Do not turn the Graph sender on in a real tenant before you complete them.

1. **Register the mail-sender app in Entra ID.** Never reuse the API's own app registration (§1) — it is a separate identity from the start.
2. **Give it a federated identity credential** for Kubernetes sign-in: issuer = the cluster's OIDC issuer, subject = `system:serviceaccount:<namespace>:notification-api` (the `<namespace>` is wherever you install the chart; the service account name is fixed), audience = `api://AzureADTokenExchange`. This is the `WorkloadIdentity` credential mode. The `ClientSecret` mode is a fallback only for a host without workload identity, or for a manual check — its secret lives in Key Vault, never a settings file.
3. **Do not grant or consent `Mail.Send` for the app in Entra ID.** An Entra ID consent for `Mail.Send` is tenant-wide and cannot be narrowed by an Exchange scope afterwards — the two grants add up, so consenting it in Entra ID defeats the scoping in the next step.
4. **Limit `Mail.Send` to the one sending mailbox in Exchange Online**, as an Exchange administrator, using RBAC for Applications:
   - add the app's service principal with `New-ServicePrincipal`;
   - create a management scope with `New-ManagementScope` whose recipient filter matches only the sending mailbox;
   - assign the role with `New-ManagementRoleAssignment -Role "Application Mail.Send" -App <app> -CustomResourceScope <scope>`.

   A tenant that already uses application access policies may use one instead: grant `Mail.Send` in Entra ID, then run `New-ApplicationAccessPolicy -AccessRight RestrictAccess` against a mail-enabled security group holding only the sending mailbox (a shared mailbox cannot be the policy's direct target). Microsoft recommends RBAC for Applications over new application access policies for a new tenant.
5. **Check the limit**: `Test-ServicePrincipalAuthorization -Identity <app> -Resource <mailbox>`. `InScope` must show `true` for the sending mailbox and `false` for any other mailbox.
6. Allow 30 minutes to 2 hours before the first send — Exchange caches app permissions for that long, so an early `403` is expected.

## 4. Chart configuration and channel setup

The chart lives at `helm/dknet-notification/`. Follow the [deployment guide](deployment.md#-deploy) to install it after configuring the values below.

### What you edit

Everything you change lives in the `operator:` block at the top of `values.yaml` — edit the file directly, don't use `--set` on the `operator.*` keys. The block's values are YAML anchors (`&name`) resolved once when the file is parsed; nothing in the chart templates reads `operator.*` directly, and `helm template --set operator.x=...` has **no effect** on the rendered output — confirmed by rendering the chart with and without such an override. If you must override at install time with `--set` or a separate values file, target the real keys the anchors feed instead, e.g. `--set api.global.tag=1.4.2` for the image tag, not `--set operator.apiImageTag=1.4.2`.

- **Image tag**: `operator.apiImageTag` (edited in `values.yaml`) or `api.global.tag` (via `--set`). Pin a released version number, e.g. `1.4.2` — never deploy `latest`. Releases are published as `ghcr.io/baoduy/dknet.notification-api:<version>` with no leading `v` on the image tag (the `v` prefix is only on the git tag/GitHub Release).
- **Azure identity**: `operator.azureTenantId`, `operator.keyVaultName`, `operator.identityClientId` (the Key Vault-reading managed identity's client id), `operator.mailSenderClientId` (the mail-sender app's client id from §3).
- **Gateway**: `operator.gatewayName`, `operator.gatewayNamespace`, `operator.apiHostname`, only if you turn on `api.httpRoute.enabled`.

### Key Vault secrets to create

The chart's default `secretProvider.objects`/`secretObjects` require exactly these 4 vault secrets to exist, named exactly as `values.yaml` gives them — a listed secret that does not exist stops the pod from starting:

| Key Vault secret name | Becomes env var |
|---|---|
| `connectionstrings--redis` | `ConnectionStrings__Redis` |
| `authentication--schemes--bearer--metadataaddress` | `Authentication__Schemes__Bearer__MetadataAddress` |
| `authentication--schemes--bearer--validaudiences-0` | `Authentication__Schemes__Bearer__ValidAudiences__0` |
| `authentication--schemes--bearer--validissuer` | `Authentication__Schemes__Bearer__ValidIssuer` |

Turning on a channel adds its own vault secret and its own `secretProvider.objects`/`secretObjects` entries, **added alongside** the 4 above — these are plain YAML lists, so a values override that replaces the whole list instead of adding to it silently drops the required defaults:

- SMTP: `notifications--email--smtp--password` → `Notifications__Email__Smtp__Password`
- Graph (with `Credential: ClientSecret`): `notifications--email--graph--clientsecret` → `Notifications__Email__Graph__ClientSecret`
- Each Teams destination: `notifications--teams--destinations--<name>--webhookurl` → `Notifications__Teams__Destinations__<name>__WebhookUrl`

For example, turning on SMTP adds one line to each list (edited directly in `values.yaml`, alongside the 4 default entries already there):

```yaml
api:
  secretProvider:
    objects:
      - notifications--email--smtp--password   # added
    secretObjects:
      - key: "Notifications__Email__Smtp__Password"   # added
        objectName: notifications--email--smtp--password
```

### Two identities, two federated credentials

This chart involves two separate Azure AD identities, easy to conflate because both authenticate the same pod through the same Kubernetes mechanism:

1. **The Key Vault-reading managed identity** (`operator.identityClientId`) — mounts secrets through the Azure Key Vault CSI driver via workload identity. It needs: `get` permission on the vault's secrets, **and** its own federated credential with the cluster's OIDC issuer as issuer and `system:serviceaccount:<namespace>:notification-api` as subject. Without that federated credential, the Key Vault secret mount fails and the pod never starts — even with every notification channel turned off.
2. **The mail-sender app** (`operator.mailSenderClientId`, §3) — signs in to Microsoft Graph as itself. Same subject format (same namespace, same fixed service account name), but it is a different client id, registered separately, and its federated credential and `Mail.Send` scoping are entirely manual (this chart creates no Entra ID objects).

Both subjects depend on the namespace you install into — set it before creating either federated credential. Verify the exact mechanics (how a federated credential binds to a service-account token, and what `useWorkloadIdentity: true`/`useVMManagedIdentity: false` mean for the CSI mount) against the Azure Key Vault Provider for Secrets Store CSI Driver's own workload identity documentation; this guide states only what this chart configures, not the general Azure mechanism.

- The service account's fixed name is `notification-api` (`api.nameOverride`/`api.fullnameOverride` in `values.yaml`), chosen deliberately so the federated credential subject is known before install.
- The Deployment's pod template carries the label `azure.workload.identity/use: "true"`; the Azure workload identity admission webhook only injects a token volume into a pod carrying that label, so without it the Graph sender finds no token file at all.
- The ServiceAccount's own `azure.workload.identity/client-id` annotation carries `operator.mailSenderClientId` — the mail-sender app's id, matching `Notifications__Email__Graph__ClientId` — never the Key Vault identity's id. The Key Vault CSI mount authenticates separately, through the `SecretProviderClass`'s own `clientID` parameter (also `operator.identityClientId`), not through the ServiceAccount annotation.

### Turning on each channel

- **SMTP**: set `Notifications__Email__Enabled: "true"` and the `Notifications__Email__Smtp__*` plain settings in `api.configMap`; add the SMTP password secret above.
- **Graph**: set `Notifications__Email__Enabled: "true"`, `Notifications__Email__Sender: "Graph"`, and the `Notifications__Email__Graph__*` plain settings; with `Credential: "ClientSecret"` also add the Graph client secret above (with `WorkloadIdentity`, no secret is needed beyond §3's federated credential).
- **Teams**: set `Notifications__Teams__Enabled: "true"` and add each destination's secret as shown above — never its webhook URL in `api.configMap`.

The shipped `account-opened` template has only an email version. A Teams call against it is accepted and skipped with `NoTemplateVersion`; a Teams smoke test needs a released template with a Teams Markdown version.

`ChannelNotConfigured` means Teams is off or its channel settings are invalid. `TeamsDestinationNotConfigured` means Teams is configured but the named destination is absent or invalid. The current `account-opened` template skips before checking its destination, so inspect destination settings directly until a Teams template version ships.

### Exposing the API

The API is `ClusterIP`-only by default. Turn on `api.httpRoute.enabled: true` (e.g. `--set api.httpRoute.enabled=true`) to attach it to the existing gateway named by `operator.gatewayName`/`operator.gatewayNamespace` at `operator.apiHostname`. This chart creates no Gateway resource itself.

## 5. Upgrade notes

For the release that adds notification status and Redis delivery (ADR-0012, ADR-0013):

- **`POST /v1/notifications` answers `200 OK` instead of `202 Accepted`.** For 4 hours after the release, an idempotent replay of a call accepted before the release still answers the stored `202`, because the idempotency records kept before it hold a `202`. New calls answer `200`. Tell callers to accept both until then.
- **`Notifications:Delivery:QueueCapacity` is for the whole service** now, not per replica. Raise it with the number of replicas (see [Redis](#redis)).
- **`notifications.queue.length` is the service-wide backlog**, the same on every replica. Rebuild dashboards and alerts that used the per-replica value.
- **Notifications that were queued in memory when the old release stopped are lost.** The old release held them in the process, so a deploy that takes it down cannot carry them over. From this release on, a deploy loses no waiting notification.
- **The Redis connection string now carries delivery and status too.** Nothing new to configure: the same `ConnectionStrings:Redis`, with the same TLS and password. Anyone who can read this Redis can now read the waiting messages too.
