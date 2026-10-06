# Deploying DKNet.Notification.Api

The release workflow publishes a container image; an operator installs the checked-in Helm chart on a Kubernetes cluster.

This guide describes repository configuration at commit `362962797612943392d6ecb73686eed9c742a574`. It does not assert which cluster runs it.

## 📦 What ships

`.github/workflows/docker-publish.yml` publishes `ghcr.io/baoduy/dknet.notification-api:<version>` and `:latest` for Linux x64 and arm64 after a push to `main` or manual dispatch. `paulhatch/semantic-version` derives the version from Git tags and commit patterns; the workflow creates a matching `v<version>` GitHub Release. Pin the numbered image tag when deploying.

The source chart is `helm/dknet-notification`, named `dknet-notification` with chart version `0.1.0` in `Chart.yaml`. The workflows lint and test it, but do not package or publish the chart. To trace a deployment, record the deployed image tag, its publishing workflow run and source commit, and the chart checkout commit used for the install. The chart version alone does not identify a published image.

## 🔄 Release path

![A dev push or pull request runs build and Helm checks; a main push runs version calculation and image publication to GHCR; an operator manually installs the chart into a cluster.](diagrams/release-path.sequence.svg)

The workflows stop at verification and image publication. Promotion to `main` and the chart installation are manual actions in this diagram; the repository names no automated deployment environment.

| Stage | Trigger | What it does | Gate |
|---|---|---|---|
| Build & Test | Push or PR to `dev`, or manual dispatch | Restore, release build and solution tests | Jobs must pass |
| Helm lint | Same `dev` triggers | Build dependency, lint, unit test and render chart variants | Jobs must pass |
| Publish Docker Image | Push to `main`, or manual dispatch | Derive version, build x64/arm64 image, push version and latest tags, create release | Workflow permissions and registry login |
| Chart install | Operator action | Resolve chart dependency and upgrade/install the selected image tag | Operator verifies prerequisites and rollout |

The chart comments describe AKS and an optional Gateway API route. The workflows name no staging or production deployment target, and the chart is not evidence that a particular cluster uses it.

## 🧱 Runtime shape

`helm/dknet-notification/values.yaml` enables one API replica by default, a `ClusterIP` Service, and HTTP port `8080`. The `drunk-app` chart dependency supplies the Deployment, Service, SecretProviderClass and optional HTTPRoute templates. The pod runs as non-root user `1654`, with a read-only root filesystem, an empty `/tmp` volume, and requests/limits of `100m/500m` CPU and `128Mi/256Mi` memory.

The chart's liveness and readiness paths both call `GET /healthz`. `HealthzConfig` reports process health only: it does not probe Redis, an email sender, Entra ID or Teams. A ready pod therefore still needs a channel smoke test.

Before installation, the chart expects a reachable Redis, the Azure Key Vault Secrets Store CSI driver, a Key Vault and authorized workload identity, and the bearer-token settings that the chart reads from secrets. An existing Gateway is needed only when `api.httpRoute.enabled` is set. The [operator guide](operator-guide.md) names the required identities, four default secrets, and channel-specific additions.

## ⚙️ Configuration and secrets

`values.yaml` uses YAML anchors in `operator:` to feed the effective `api:` values. Edit those anchors in a values file before installation; a `--set operator.*` override does not change the aliased values. `api.configMap` supplies plain environment variables and `api.secretProvider` maps Key Vault secrets to Kubernetes secret-backed environment variables. The chart sets `ASPNETCORE_ENVIRONMENT=Production`, and the application refuses to start there without `ConnectionStrings:Redis`.

Use the [operator configuration and channel setup guide](operator-guide.md) for the settings, secret names and SMTP, Microsoft Graph and Teams setup. It links back here for installation and checks.

## 🗃️ Database changes

There is no relational database migration in this repository. The delivery list and status keys are Redis data shapes created by the running service. A new image can consume queued `DeliverNotification` messages that use the current optional-field schema, but the repository does not prove general backward compatibility for every future release. A rollback does not restore popped messages, expired status records or provider side effects.

## 🚀 Deploy

Prepare the prerequisites and values described in the [operator guide](operator-guide.md), including a numbered `api.global.tag`, the four default vault secrets, and any channel secrets. From the repository root:

```sh
cd helm/dknet-notification
NAMESPACE=your-namespace
helm dependency build .
helm upgrade --install notification-api . -n "$NAMESPACE" --create-namespace
```

The `helm dependency build` step pulls the `drunk-app` dependency. The upgrade/install creates or replaces cluster resources and can restart the API pods. If using an external values file, pass it explicitly with `-f <values-file>`; if overriding the image tag on the command line, target `api.global.tag`, not `operator.apiImageTag`. The chart defaults to `latest`; replace it with a numbered release tag before deploying.

## ✅ Verify

1. Check the Deployment rollout and pod readiness. Both probes call `/healthz`, which should answer `200` with `{"status":"Healthy"}`; this only proves the process is responsive.
2. Send one [email notification](features/send-notification.md#-quick-start) with a new idempotency key, a real token for a caller with `notifications.send`, and a template/channel configured for this release. Expect `200` with `notificationId`.
3. [Poll status](features/notification-status.md#-quick-start) as the same caller. Expect `pending` while queued and then `success` when the provider accepts it. A `failed` result requires investigation; an accepted `200` may have been skipped.
4. Inspect structured `NotificationQueued`, `NotificationDelivered`, `NotificationFailed` or `NotificationSkipped` entries and the `notifications.queue.length` gauge. Each replica reports the shared Redis backlog; do not sum replicas.
5. For Graph workload identity, confirm the pod has `azure.workload.identity/use: "true"`, `AZURE_FEDERATED_TOKEN_FILE` is set and that file exists. Check mailbox scoping with `Test-ServicePrincipalAuthorization` as described in the [operator guide](operator-guide.md#3-microsoft-graph-setup-required-before-you-turn-the-graph-sender-on).

The shipped `account-opened` template has no Teams version. A Teams smoke call needs another released template with a Teams version; see [channel setup](operator-guide.md#turning-on-each-channel).

The repository specifies no observation window or service objective; agree those before treating a rollout as complete.

## ↩️ Roll back

If a new image fails the checks above, inspect the release history and select the last verified revision, then run the Helm rollback from `helm/dknet-notification`:

```sh
NAMESPACE=your-namespace
helm history notification-api -n "$NAMESPACE"
PREVIOUS_REVISION=REPLACE_WITH_REVIEWED_REVISION
helm rollback notification-api "$PREVIOUS_REVISION" -n "$NAMESPACE"
```

Recheck rollout, `/healthz`, one send and its status. Rollback changes the chart release and running image; it does not undo messages already delivered, Redis writes or lost list items. The chart has no relational migration to reverse. Preserve the Redis queue across restarts and avoid a flush. The supported recovery target and rollback owner remain open questions.

## 🧯 When a deploy fails

| What you see | Likely cause | What to do |
|---|---|---|
| Pod does not start and secret mount fails | A Key Vault secret or identity permission is missing | Compare the chart's `secretProvider` entries with the [operator guide](operator-guide.md#key-vault-secrets-to-create) |
| Pod fails at startup | Missing Redis string in Production, invalid delivery/status settings, or broken template catalogue | Correct configuration; inspect startup logs without copying secret values |
| `/healthz` is healthy but status read or send fails | Probe does not check Redis or providers | Check Redis reachability and provider settings, then repeat the feature smoke call |
| Send returns `503 QUEUE_FULL` | Shared Redis list reached `QueueCapacity` | Inspect backlog and consumer/provider health; honor `Retry-After: 30` |
| Send returns `200` but status is `failed` | Channel or template version was skipped, or delivery ended failed | Inspect `NotificationSkipped` or `NotificationFailed` logs and [channel setup](operator-guide.md#turning-on-each-channel) |

## ❓ Open questions

| Question | Why it matters | Checked | Who can answer |
|---|---|---|---|
| Which environment and release approval process use this chart? | Determines the promotion and rollback path. | Both workflow YAML files and chart | Project owner |
| Who owns release support and what is the response route? | Directs failed rollout escalation. | Workflow and repository docs | Project owner |
| What are the observation window, service objective and Redis recovery targets? | Sets release and rollback decisions. | Chart, health check and operator guide | Service owner |
