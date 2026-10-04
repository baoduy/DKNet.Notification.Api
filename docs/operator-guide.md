# Operator guide

Audience: operators deploying and configuring DKNet.Notification.Api on Kubernetes. You know AKS, Helm, Azure Key Vault and Entra ID; you do not read the code. This guide is the only thing you need to install the service and turn on email (SMTP or Microsoft Graph) and Microsoft Teams delivery.

## 1. Prerequisites

Before you install the chart, have ready:

- An AKS cluster with its OIDC issuer and workload identity enabled. Both federated credentials below (Key Vault identity, mail-sender app) use that issuer.
- The Azure Key Vault Secrets Store CSI driver installed on the cluster.
- An existing Redis instance reachable from the cluster. The service refuses to start in production without a connection string to it — see [§2 Redis](#redis).
- An Azure Key Vault holding the service's secrets (§2 lists them).
- A user-assigned managed identity with `get` permission on that vault's secrets, with workload identity federation to the cluster's service account.
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
| `Notifications:Delivery:QueueCapacity` | `Notifications__Delivery__QueueCapacity` | `1000` | No | Plain settings. 1–100,000, per replica |
| `Notifications:Delivery:MaxAttempts` | `Notifications__Delivery__MaxAttempts` | `3` | No | Plain settings. 1–3 |
| `Notifications:Delivery:RetryDelaysSeconds` | `Notifications__Delivery__RetryDelaysSeconds__0/1` | `[5, 30]` | No | Plain settings. Exactly 2 values, each 1–300 seconds |

**A bad `Delivery` value stops the service from starting.** A bad `Email` or `Teams` value does not — it only leaves that channel unable to send (see "What a bad value does" below).

### Email senders

Exactly one sender is active per deployment, chosen by `Notifications:Email:Sender`.

**SMTP** (`Notifications:Email:Smtp:*`, read only when `Sender` is `Smtp`):

| Key | Env var | Default | Secret? |
|---|---|---|---|
| `Host` | `Notifications__Email__Smtp__Host` | *(empty — required)* | No |
| `Port` | `Notifications__Email__Smtp__Port` | `587` | No |
| `Security` | `Notifications__Email__Smtp__Security` | `StartTls` | No. `StartTls` or `Tls` — no plain-text mode exists |
| `UserName` | `Notifications__Email__Smtp__UserName` | *(empty — no sign-in)* | No |
| `Password` | `Notifications__Email__Smtp__Password` | *(empty)* | **Yes — Key Vault only, never a settings file** |
| `FromAddress` | `Notifications__Email__Smtp__FromAddress` | *(empty — required)* | No |
| `FromName` | `Notifications__Email__Smtp__FromName` | `DKNet Notification` | No |

**Microsoft Graph** (`Notifications:Email:Graph:*`, read only when `Sender` is `Graph` — see §3 before turning this on):

| Key | Env var | Default | Secret? |
|---|---|---|---|
| `TenantId` | `Notifications__Email__Graph__TenantId` | *(empty — required GUID)* | No |
| `ClientId` | `Notifications__Email__Graph__ClientId` | *(empty — required GUID)* | No |
| `Credential` | `Notifications__Email__Graph__Credential` | `WorkloadIdentity` | No. `WorkloadIdentity` or `ClientSecret` |
| `ClientSecret` | `Notifications__Email__Graph__ClientSecret` | *(empty)* | **Yes, only with `Credential=ClientSecret` — Key Vault only** |
| `Mailbox` | `Notifications__Email__Graph__Mailbox` | *(empty — required)* | No. The one sending mailbox |

### Teams destinations

- Up to 100 destinations, each a name → webhook URL pair under `Notifications:Teams:Destinations:<name>:WebhookUrl`.
- A webhook URL must be an absolute `https://` URL of at most 2,048 characters, or the destination is treated as not set.
- **Destination names are matched with case**, and a caller may only ever name a destination of 1–64 lowercase letters, digits or `-` — a caller can never reach a destination named with an uppercase letter, so always name destinations in lowercase.
- The destination name comes from the environment variable key the chart writes (`secretObjects[].key`'s last path segment), **not** from the Key Vault secret name: Key Vault secret names allow only letters, digits and `-` (no `_`), so the chart's convention is `__` → `--`, lowercased, on the Key Vault side, while the env var key keeps the real `Notifications__Teams__Destinations__<name>__WebhookUrl` form. Example: the vault secret `notifications--teams--destinations--ops-alerts--webhookurl` becomes the env var `Notifications__Teams__Destinations__ops-alerts__WebhookUrl`, which is the destination name `ops-alerts`.
- **Never put a webhook URL in the chart's plain `configMap` settings.** Anyone who can read the ConfigMap can then post to that Teams channel. A destination must be created through its Key Vault secret alone (`secretProvider.objects` + `secretProvider.secretObjects`), never through `api.configMap`. The chart's own CI template run exercises an empty `WebhookUrl` string directly in `configMap` to prove the chart renders — that CI convenience is not a deployment pattern to copy.

### Templates

- `Notifications:Templates` is a list of template registrations; each names an id, a description and one version per channel (`email` → HTML, `teams` → Markdown), and each version's file is loaded from the `Templates` folder inside the container image at start-up.
- The container's root filesystem is read-only in the chart (`securityContext.readOnlyRootFilesystem: true`), so **a new template file can only ship in a new image** — you cannot add or edit a template file at deploy time through settings.
- You *can* change a shipped template's metadata (description, subject, title) through settings, because `Notifications:Templates` is bound as an indexed list: an environment variable such as `Notifications__Templates__0__Versions__0__Subject` overwrites that field of the shipped `account-opened` entry at index 0. Adding a new index (e.g. index `1`) adds a template registration, but its file must already exist in the image.
- A broken catalogue — a bad id, a missing file, two versions for one channel, a subject over 500 characters — **stops the service from starting**.

### What a bad value does

| Setting group | A bad value does this |
|---|---|
| `Notifications:Delivery:*` | Stops the service from starting |
| `Notifications:Templates` (the catalogue) | Stops the service from starting |
| `Notifications:Email:*` / `Notifications:Email:Smtp:*` / `Notifications:Email:Graph:*` | The service starts; email is left "not configured" and every email call is skipped (logged once at start-up as a warning, naming the bad settings — never their values) |
| `Notifications:Teams:*` | The service starts; Teams is left "not configured" and every Teams call to it is skipped — **no log entry is written for this one**, so check `TeamsDestinationNotConfigured` on a per-call basis instead (see §5) |

### Redis

- `ConnectionStrings:Redis` holds the idempotency store's connection string. The service reads it for both request idempotency and the general distributed cache.
- **In `Production` (the chart's `ASPNETCORE_ENVIRONMENT`), a missing `ConnectionStrings:Redis` stops the service from starting.** The in-memory fallback exists only for local `Development` and `Testing` runs — it is not a production degrade-to-single-replica path.
- More than one replica needs Redis for a second reason even where it did start: without it, idempotency and the distributed cache would each keep separate in-memory state per replica, so a retried call could be treated as new on a different replica. The chart lists the Redis secret in its default `secretProvider.objects`/`secretObjects`, so it is required, not opt-in.

### The other settings the chart sets or needs

| Key | Chart default | Notes |
|---|---|---|
| `Authentication:Schemes:Bearer:MetadataAddress` / `ValidIssuer` / `ValidAudiences:0` | From Key Vault | The API's own Entra ID app registration (§1); required for callers to authenticate |
| `FeatureManagement:RequireAuthorization` | `"true"` | Must never be `"false"` in a real deployment — it is the only thing requiring a bearer token on every route |
| `FeatureManagement:EnableHttps` | `"false"` | TLS ends at the gateway in front of this Service, not in the container |
| `Security:TrustedProxies:0` | Commented out | Uncomment together with `operator.trustedProxy` only once you know the gateway's real peer address — an **empty** value here fails `IPAddress.Parse` and crashes the service at start-up |

## 3. Microsoft Graph setup (required before you turn the Graph sender on)

Skip this section if you use the SMTP sender.

1. **Register the mail-sender app in Entra ID.** Never reuse the API's own app registration (§1) — it is a separate identity from the start.
2. **Give it a federated identity credential** for Kubernetes sign-in: issuer = the cluster's OIDC issuer, subject = `system:serviceaccount:<namespace>:notification-api` (the `<namespace>` is wherever you install the chart; the service account name is fixed), audience = `api://AzureADTokenExchange`. This is the `WorkloadIdentity` credential mode. The `ClientSecret` mode is a fallback only for a host without workload identity, or for a manual check — its secret lives in Key Vault, never a settings file.
3. **Do not grant or consent `Mail.Send` for the app in Entra ID.** An Entra ID consent for `Mail.Send` is tenant-wide and cannot be narrowed by an Exchange scope afterwards — the two grants add up, so consenting it in Entra ID defeats the scoping in the next step.
4. **Limit `Mail.Send` to the one sending mailbox in Exchange Online**, as an Exchange administrator, using RBAC for Applications:
   - add the app's service principal with `New-ServicePrincipal`;
   - create a management scope with `New-ManagementScope` whose recipient filter matches only the sending mailbox;
   - assign the role with `New-ManagementRoleAssignment -Role "Application Mail.Send" -App <app> -CustomResourceScope <scope>`.

   A tenant that already uses application access policies may use one instead: grant `Mail.Send` in Entra ID, then run `New-ApplicationAccessPolicy -AccessRight RestrictAccess` against a mail-enabled security group holding only the sending mailbox (a shared mailbox cannot be the policy's direct target). Microsoft recommends RBAC for Applications over new application access policies for a new tenant.
5. **Without this limit, the app can send as any mailbox in the tenant.**
6. **Check the limit**: `Test-ServicePrincipalAuthorization -Identity <app> -Resource <mailbox>`. `InScope` must show `true` for the sending mailbox and `false` for any other mailbox.
7. Allow 30 minutes to 2 hours before the first send — Exchange caches app permissions for that long, so an early `403` is expected.

## 4. Deploy with the Helm chart

The chart is `helm/dknet-notification/` in this repo. CI lints and unit-tests it but publishes it nowhere, so install straight from the checked-out folder:

```bash
cd helm/dknet-notification
helm dependency build
helm upgrade --install notification-api . -n <namespace> --create-namespace
```

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

### Exposing the API

The API is `ClusterIP`-only by default. Turn on `api.httpRoute.enabled: true` (e.g. `--set api.httpRoute.enabled=true`) to attach it to the existing gateway named by `operator.gatewayName`/`operator.gatewayNamespace` at `operator.apiHostname`. This chart creates no Gateway resource itself.

## 5. Checks after deploy

1. **Pod readiness**: the Deployment's liveness and readiness probes both hit `GET /healthz`, which reports process liveness only — no check of Redis, SMTP, Graph, the Entra ID token endpoint or Teams. A pod reporting ready does not by itself prove any channel can send.
2. **With the Graph sender on `Credential: WorkloadIdentity`**, confirm the token actually reached the pod — this is the only proof available before a real tenant is involved, since no cluster has run this chart yet:
   - the pod carries the label `azure.workload.identity/use: "true"`;
   - the pod's environment holds `AZURE_FEDERATED_TOKEN_FILE` (injected by the workload identity webhook, not set by this chart);
   - that file exists inside the container. The chart disables the Kubernetes-default service account token automount (`automountServiceAccountToken: false`), so this projected file is the pod's only source of a token — if it is missing, the Graph sender cannot sign in.
3. **Run `Test-ServicePrincipalAuthorization`** for the mail-sender app and sending mailbox (§3, step 6) — confirm again after any mailbox or scope change.
4. **Send one test notification per channel you turned on**, as a caller holding the `notifications.send` scope or role, against `POST /v1/notifications`:

   ```json
   {"channel": "email", "templateId": "account-opened", "parameters": {"to": "test@example.com", "customerName": "Test", "accountNumber": "0000"}}
   ```

   ```json
   {"channel": "teams", "templateId": "account-opened", "parameters": {"teamsDestination": "<your-destination-name>", "customerName": "Test", "accountNumber": "0000"}}
   ```

   Both answers are `202 Accepted` whether the notification was actually queued or silently skipped — **the caller cannot tell the difference from the response alone**. Check the structured logs for the outcome: `NotificationQueued` then `NotificationDelivered` or `NotificationFailed` for a real send, or `NotificationSkipped` (with its `Reason`) when a channel is not configured or the template has no version for that channel.

   The shipped `account-opened` template has only an `email` version in this release — a `teams` test call against it answers 202 and then logs `NotificationSkipped` with reason `NoTemplateVersion`, not a delivery. A real Teams delivery test needs a template release that registers a Teams (Markdown) version of a template; this guide cannot make that call for you.

   A bad Teams setting (an unset destination, a bad webhook URL) writes **no** log entry distinguishing it from a correctly-skipped call beyond `TeamsDestinationNotConfigured` as the skip reason — there is no separate start-up warning for Teams the way there is for email.
