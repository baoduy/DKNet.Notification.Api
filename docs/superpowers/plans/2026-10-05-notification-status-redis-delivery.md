# Notification Status and Redis-Queue Delivery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `GET /v1/notifications/{id}` (pending / success / failed, 24 h, caller-scoped), move delivery from the in-process queue onto a SlimMessageBus Redis queue whose retries survive a restart, and answer `POST /v1/notifications` with 200.

**Architecture:** The SMB hybrid bus gets two child buses: `Mediator` (memory, unchanged role, ADR-0011) and `Delivery` (Redis queue `notification-delivery`, or a non-blocking memory topic in Development/Testing without Redis). `SendNotificationService` checks the backlog, writes `pending` to an `IDistributedCache` status store, then publishes `DeliverNotification`. `DeliveryConsumer` makes one attempt per message and either writes a final status or re-publishes the message with its next attempt time.

**Tech Stack:** .NET 10, SlimMessageBus 3.5.0 (`Host.Memory`, `Host.Redis`, `Host.Serialization.SystemTextJson`), StackExchange.Redis (transitive), `IDistributedCache`, xUnit + Shouldly, Reqnroll + NUnit, Testcontainers Redis.

**Spec:** `docs/superpowers/specs/2026-10-05-notification-status-redis-delivery-design.md`

## Global Constraints

- Layer rule: `Api` → `AppServices` → `Domains` → `Share`. Domain types in `DKNet.Notification.Domains`; services, consumers, validators in `DKNet.Notification.AppServices`.
- Production projects build with warnings as errors (analyzers on). Every new public member needs an XML doc comment in the house style (short, plain English).
- Time: production code reads the injected `TimeProvider`, never `DateTimeOffset.UtcNow`, never a plain `Task.Delay` without the `TimeProvider` overload. Parsing a `Retry-After` date stays on the real clock (already the case in `RetryAfterWait`).
- Never log a recipient, a parameter value, a rendered subject/body or a webhook URL. Caller text reaches a log only through `SanitizeForLogging()`.
- Public status values are exactly `pending`, `success`, `failed`. Skipped is written as `failed`.
- Status key: `status:{callerId}:{notificationId}`. Retention setting `Notifications:Status:RetentionHours`, default 24, valid 1–168.
- Delivery queue name: `notification-delivery`. Consumer `Instances(1)`.
- `POST /v1/notifications` success answer: `200 OK { "notificationId": "…" }`. No `Location` header.
- `GET /v1/notifications/{notificationId:guid}` 404 code: `NOTIFICATION_NOT_FOUND`. Response header `Cache-Control: no-store`.
- New packages (central in `Directory.Packages.props`, version 3.5.0): `SlimMessageBus.Host.Redis`, `SlimMessageBus.Host.Serialization.SystemTextJson`.
- Package versions are pinned centrally; never put a `Version=` on a `PackageReference`.
- Tests: business behaviour only (AGENTS.md). App.Tests classes run in parallel; a class listening to something process-wide joins `[Collection(SerialTestsCollection.Name)]`.
- Commit after each task. Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A message published by a previous release's host, or after a restart, is still delivered.** Covered by the BDD restart scenario in Task 7 (stop a host during a retry wait, start a new host on the same Redis, the mail arrives).
2. **A status never goes backwards** (`success`/`failed` overwritten by `pending`). Covered in Task 4: the test asserts the `pending` write happens before the publish (a publisher fake that reads the store at publish time sees `pending`).
3. **Another caller's id answers 404, not 200 or 403.** Covered by the route test in Task 6.
4. **A message that is not due yet does not hold up a due one behind it.** Covered by the existing BDD scenario "A Teams notification waiting after a 429 does not hold up the next one" (kept, Task 7) and the consumer unit test "not due → re-published unchanged and returns within about a second" (Task 3).
5. **The host stopping in the middle of an attempt puts the message back with its attempt count unchanged.** Covered by the consumer unit test in Task 3 (cancellation during `SendAsync`).

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `Directory.Packages.props` | Modify | Add the two SMB packages. |
| `ApiEndpoints/DKNet.Notification.Api/DKNet.Notification.Api.csproj` | Modify | Reference the two SMB packages. |
| `ApiEndpoints/DKNet.Notification.Domains/Notifications/Notification.cs` | Modify | Add `Notification.Resume(...)`. |
| `ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationOutcome.cs` | Create | `Pending`, `Success`, `Failed`. |
| `ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationStatusRecord.cs` | Create | The stored status record. |
| `ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationStatusSettings.cs` | Create | `RetentionHours` + `Validate()`. |
| `ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationStatusStore.cs` | Create | Read/write over `IDistributedCache`. |
| `ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationLog.cs` | Modify | Add `NotificationStatusWriteFailed` (2007), `NotificationRequeued` (2008); `NotificationQueued` keeps its shape. |
| `ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationErrorCodes.cs` | Modify | Add `NotificationNotFound = "NOTIFICATION_NOT_FOUND"`. |
| `ApiEndpoints/DKNet.Notification.AppServices/Delivery/DeliverNotification.cs` | Create | The queued message + `DeliveryQueueName` constant. |
| `ApiEndpoints/DKNet.Notification.AppServices/Delivery/IDeliveryBacklog.cs` | Create | `ValueTask<long> LengthAsync(CancellationToken)` + `InProcessDeliveryBacklog` (always 0). |
| `ApiEndpoints/DKNet.Notification.AppServices/Delivery/DeliveryConsumer.cs` | Create | One attempt per message (replaces `DeliveryWorker`). |
| `ApiEndpoints/DKNet.Notification.AppServices/Delivery/DeliveryWorker.cs`, `DeliveryQueue.cs`, `QueuedNotification.cs` | Delete | Replaced. |
| `ApiEndpoints/DKNet.Notification.AppServices/Notifications/SendNotification.cs` | Modify | Add `IdempotencyKey`; handler awaits `SendAsync`. |
| `ApiEndpoints/DKNet.Notification.AppServices/Notifications/SendNotificationService.cs` | Modify | Async; backlog check, `pending` write, publish; Skipped writes `failed`. |
| `ApiEndpoints/DKNet.Notification.AppServices/Notifications/GetNotificationStatus.cs` | Create | Mediator request + `internal sealed` handler. |
| `ApiEndpoints/DKNet.Notification.Api/Configs/ServiceConfigs.cs` | Modify | Child buses, Redis or memory delivery. |
| `ApiEndpoints/DKNet.Notification.Api/Configs/RedisDeliveryBacklog.cs` | Create | `LLEN notification-delivery`. |
| `ApiEndpoints/DKNet.Notification.Api/Configs/EmailConfig.cs` | Modify | Drop `DeliveryQueue`/`DeliveryWorker`/host; bind status settings. |
| `ApiEndpoints/DKNet.Notification.Api/Configs/DeliveryWorkerHost.cs` | Delete | Replaced by SMB's hosted bus. |
| `ApiEndpoints/DKNet.Notification.Api/Configs/LogConfigs.cs` | Modify | Activity source constant moves to `DeliveryConsumer.ActivitySourceName`. |
| `ApiEndpoints/DKNet.Notification.Api/Configs/FluentValidationConfig.cs` | Modify | `NOTIFICATION_NOT_FOUND` → 404. |
| `ApiEndpoints/DKNet.Notification.Api/ApiEndpoints/Notifications/NotificationsV1Endpoint.cs` | Modify | POST → 200 + key; new GET. |
| `ApiEndpoints/DKNet.Notification.Api/ApiEndpoints/Notifications/NotificationStatusResponse.cs` | Create | `{ notificationId, idempotencyKey, status }`. |
| `ApiEndpoints/DKNet.Notification.Api/appsettings.json` | Modify | `Notifications:Status:RetentionHours: 24`. |
| Tests (App.Tests, BDD) | Modify/Create | See each task. |
| `docs/architect/**`, `docs/operator-guide.md`, `AGENTS.md` | Modify/Create | Task 8. |

---

### Task 1: Packages and `Notification.Resume`

**Files:**
- Modify: `Directory.Packages.props` (next to line 74, `SlimMessageBus.Host.Memory`)
- Modify: `ApiEndpoints/DKNet.Notification.Api/DKNet.Notification.Api.csproj` (next to line 41)
- Modify: `ApiEndpoints/DKNet.Notification.Domains/Notifications/Notification.cs`
- Test: `ApiEndpoints/DKNet.Notification.App.Tests/Unit/Notifications/NotificationTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public static Notification Resume(
      Guid notificationId, string templateId, string channel, string callerId, DateTimeOffset acceptedAt,
      EmailRecipient? recipient, TeamsRecipient? teamsRecipient, RenderedMessage renderedMessage, int attemptsMade);
  ```
  Returns a notification in `Queued` when `attemptsMade == 0`, otherwise `RetryWaiting` with `AttemptCount == attemptsMade`. Throws `ArgumentOutOfRangeException` when `attemptsMade` is outside `0..MaxAttempts-1`, `ArgumentException` when neither or both recipients are given. `Parameters` is an empty dictionary (parameters are not carried in the queue).

- [ ] **Step 1: Add the packages**

`Directory.Packages.props`, beside `SlimMessageBus.Host.Memory`:
```xml
    <PackageVersion Include="SlimMessageBus.Host.Redis" Version="3.5.0" />
    <PackageVersion Include="SlimMessageBus.Host.Serialization.SystemTextJson" Version="3.5.0" />
```
`DKNet.Notification.Api.csproj`, beside `SlimMessageBus.Host.Memory`:
```xml
        <PackageReference Include="SlimMessageBus.Host.Redis" />
        <PackageReference Include="SlimMessageBus.Host.Serialization.SystemTextJson" />
```
Run: `dotnet build -c Release` from the solution root. Expected: build succeeds. **If NuGet reports a StackExchange.Redis version conflict** with `Microsoft.Extensions.Caching.StackExchangeRedis` or `DKNet.AspCore.Idempotency.RedisStore`, add `<PackageVersion Include="StackExchange.Redis" Version="<the highest version any of them needs>" />` centrally and say so in the task report.

- [ ] **Step 2: Write the failing tests** (append to `NotificationTests.cs`, keep the file's style)

```csharp
    private static readonly RenderedMessage Rendered = new("Your account is open", "Dear Jane", BodyFormat.Html);

    [Fact]
    public void A_resumed_notification_with_no_attempt_made_is_queued()
    {
        EmailRecipient.TryCreate("jane@example.com", out var to).ShouldBeTrue();
        var id = Guid.CreateVersion7();
        var acceptedAt = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

        var notification = Notification.Resume(id, "account-opened", "email", "treasury-ops", acceptedAt, to, null, Rendered, 0);

        notification.NotificationId.ShouldBe(id);
        notification.Status.ShouldBe(NotificationStatus.Queued);
        notification.AttemptCount.ShouldBe(0);
        notification.Recipient.ShouldBe(to);
        notification.RenderedMessage.ShouldBe(Rendered);
        notification.AcceptedAt.ShouldBe(acceptedAt);
        notification.Parameters.ShouldBeEmpty();
    }

    [Fact]
    public void A_resumed_notification_with_attempts_made_waits_for_its_next_attempt_and_still_gets_at_most_3()
    {
        TeamsRecipient.TryCreate("ops-alerts", out var to).ShouldBeTrue();

        var notification = Notification.Resume(Guid.CreateVersion7(), "staff-account-opened", "teams", "treasury-ops",
            DateTimeOffset.UnixEpoch, null, to, Rendered, 2);

        notification.Status.ShouldBe(NotificationStatus.RetryWaiting);
        notification.AttemptCount.ShouldBe(2);
        notification.TeamsRecipient.ShouldBe(to);
        notification.StartAttempt();
        notification.AttemptCount.ShouldBe(3);
        notification.WaitForRetry();
        Should.Throw<InvalidOperationException>(notification.StartAttempt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void A_notification_cannot_resume_outside_its_attempts(int attemptsMade)
    {
        EmailRecipient.TryCreate("jane@example.com", out var to).ShouldBeTrue();
        Should.Throw<ArgumentOutOfRangeException>(() => Notification.Resume(Guid.CreateVersion7(), "t", "email", "c",
            DateTimeOffset.UnixEpoch, to, null, Rendered, attemptsMade));
    }

    [Fact]
    public void A_notification_resumes_with_exactly_one_recipient()
    {
        EmailRecipient.TryCreate("jane@example.com", out var email).ShouldBeTrue();
        TeamsRecipient.TryCreate("ops-alerts", out var teams).ShouldBeTrue();
        Should.Throw<ArgumentException>(() => Notification.Resume(Guid.CreateVersion7(), "t", "email", "c",
            DateTimeOffset.UnixEpoch, null, null, Rendered, 0));
        Should.Throw<ArgumentException>(() => Notification.Resume(Guid.CreateVersion7(), "t", "email", "c",
            DateTimeOffset.UnixEpoch, email, teams, Rendered, 0));
    }
```

- [ ] **Step 3: Run to see them fail**

Run: `dotnet test ApiEndpoints/DKNet.Notification.App.Tests --filter "FullyQualifiedName~NotificationTests"`
Expected: compile error, `Notification` has no `Resume`.

- [ ] **Step 4: Implement**

In `Notification.cs`, change the private constructor to take the id (the existing `Receive` passes `Guid.CreateVersion7()`):
```csharp
    private Notification(
        Guid notificationId,
        string templateId,
        string channel,
        IReadOnlyDictionary<string, string> parameters,
        string callerId,
        DateTimeOffset acceptedAt)
    {
        NotificationId = notificationId;
        TemplateId = templateId;
        Channel = channel.ToLowerInvariant();
        Parameters = parameters;
        CallerId = callerId;
        AcceptedAt = acceptedAt;
    }
```
`Receive` becomes `new(Guid.CreateVersion7(), templateId, channel, parameters, callerId, acceptedAt)`. Keep the existing `CA1308` suppression on the constructor. Add after `Receive`:
```csharp
    /// <summary>
    ///     Rebuilds a queued notification from the delivery queue: Queued when no attempt was made yet, RetryWaiting
    ///     otherwise. Its parameters are not carried in the queue, so they are empty.
    /// </summary>
    /// <param name="notificationId">The id the caller got back.</param>
    /// <param name="templateId">The template id the caller named.</param>
    /// <param name="channel">The channel, already in lower case.</param>
    /// <param name="callerId">The calling application's id.</param>
    /// <param name="acceptedAt">When the call was accepted.</param>
    /// <param name="recipient">The email address, for an email notification.</param>
    /// <param name="teamsRecipient">The Teams destination, for a Teams notification.</param>
    /// <param name="renderedMessage">The filled subject or title and the body.</param>
    /// <param name="attemptsMade">Delivery attempts already made: 0 to 2.</param>
    /// <returns>A notification ready for its next attempt.</returns>
    public static Notification Resume(
        Guid notificationId,
        string templateId,
        string channel,
        string callerId,
        DateTimeOffset acceptedAt,
        EmailRecipient? recipient,
        TeamsRecipient? teamsRecipient,
        RenderedMessage renderedMessage,
        int attemptsMade)
    {
        ArgumentNullException.ThrowIfNull(renderedMessage);
        ArgumentOutOfRangeException.ThrowIfNegative(attemptsMade);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(attemptsMade, MaxAttempts);
        if ((recipient is null) == (teamsRecipient is null))
        {
            throw new ArgumentException("A notification resumes with exactly one recipient.", nameof(recipient));
        }

        var notification = new Notification(
            notificationId, templateId, channel, new Dictionary<string, string>(), callerId, acceptedAt)
        {
            Status = attemptsMade == 0 ? NotificationStatus.Queued : NotificationStatus.RetryWaiting,
            AttemptCount = attemptsMade,
            Recipient = recipient,
            TeamsRecipient = teamsRecipient,
            RenderedMessage = renderedMessage
        };
        return notification;
    }
```
(The properties have `private set`, so the object initializer compiles inside the class.)

- [ ] **Step 5: Run the tests**

Run: `dotnet test ApiEndpoints/DKNet.Notification.App.Tests --filter "FullyQualifiedName~NotificationTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props ApiEndpoints/DKNet.Notification.Api/DKNet.Notification.Api.csproj ApiEndpoints/DKNet.Notification.Domains/Notifications/Notification.cs ApiEndpoints/DKNet.Notification.App.Tests/Unit/Notifications/NotificationTests.cs
git commit -m "feat: resume a queued notification from the delivery queue"
```

---

### Task 2: Status store

**Files:**
- Create: `ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationOutcome.cs`
- Create: `ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationStatusRecord.cs`
- Create: `ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationStatusSettings.cs`
- Create: `ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationStatusStore.cs`
- Modify: `ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationLog.cs`
- Test: `ApiEndpoints/DKNet.Notification.App.Tests/Unit/Notifications/NotificationStatusStoreTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum NotificationOutcome { Pending, Success, Failed }
  public sealed record NotificationStatusRecord(Guid NotificationId, string? IdempotencyKey, NotificationOutcome Status);
  public sealed class NotificationStatusSettings(int retentionHours = 24)
  { public const string SectionName = "Notifications:Status"; public int RetentionHours { get; } public void Validate(); }
  public sealed class NotificationStatusStore(IDistributedCache cache, NotificationStatusSettings settings, ILogger<NotificationStatusStore> logger)
  {
      public Task WriteAsync(string callerId, NotificationStatusRecord record, CancellationToken cancellationToken); // never throws (except null args)
      public Task<NotificationStatusRecord?> ReadAsync(string callerId, Guid notificationId, CancellationToken cancellationToken); // throws when the cache cannot be read
      public static string KeyOf(string callerId, Guid notificationId); // "status:{callerId}:{notificationId}"
  }
  ```
  Log: `NotificationLog.NotificationStatusWriteFailed(this ILogger, Guid notificationId, NotificationOutcome status, string callerId)` — EventId 2007, Warning, no exception text (it may hold a connection string).

- [ ] **Step 1: Write the failing tests**

```csharp
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.AppServices.Notifications;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>Spec §7: a status record is the caller's own, lives for the retention time, and a failed write never throws.</summary>
public sealed class NotificationStatusStoreTests
{
    private readonly TestLogCapture _logs = new();

    private NotificationStatusStore Store(IDistributedCache cache, int retentionHours = 24) =>
        new(cache, new NotificationStatusSettings(retentionHours), new LoggerFactory([_logs]).CreateLogger<NotificationStatusStore>());

    private static MemoryDistributedCache Cache() => new(Options.Create(new MemoryDistributedCacheOptions()));

    [Fact]
    public async Task A_written_status_is_read_back_by_its_caller_only()
    {
        var store = Store(Cache());
        var record = new NotificationStatusRecord(Guid.CreateVersion7(), "order-42", NotificationOutcome.Pending);

        await store.WriteAsync("treasury-ops", record, CancellationToken.None);

        (await store.ReadAsync("treasury-ops", record.NotificationId, CancellationToken.None)).ShouldBe(record);
        (await store.ReadAsync("someone-else", record.NotificationId, CancellationToken.None)).ShouldBeNull();
        (await store.ReadAsync("treasury-ops", Guid.CreateVersion7(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_later_write_replaces_the_status()
    {
        var store = Store(Cache());
        var id = Guid.CreateVersion7();
        await store.WriteAsync("c", new NotificationStatusRecord(id, null, NotificationOutcome.Pending), CancellationToken.None);
        await store.WriteAsync("c", new NotificationStatusRecord(id, null, NotificationOutcome.Success), CancellationToken.None);

        (await store.ReadAsync("c", id, CancellationToken.None))!.Status.ShouldBe(NotificationOutcome.Success);
    }

    [Fact]
    public async Task A_status_expires_after_the_retention_time_from_its_last_write()
    {
        var cache = new RecordingCache();
        var store = Store(cache, retentionHours: 6);

        await store.WriteAsync("c", new NotificationStatusRecord(Guid.CreateVersion7(), null, NotificationOutcome.Failed), CancellationToken.None);

        cache.LastOptions!.AbsoluteExpirationRelativeToNow.ShouldBe(TimeSpan.FromHours(6));
        cache.LastKey.ShouldStartWith("status:c:");
    }

    [Fact]
    public async Task A_failed_write_is_logged_and_does_not_throw()
    {
        var store = Store(new BrokenCache());
        var id = Guid.CreateVersion7();

        await store.WriteAsync("treasury-ops", new NotificationStatusRecord(id, null, NotificationOutcome.Pending), CancellationToken.None);

        var entry = _logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Value("NotificationId").ShouldBe(id.ToString());
        entry.Value("Status").ShouldBe("Pending");
        _logs.Messages.ShouldAllBe(m => !m.Contains("redis-down", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(169)]
    public void A_retention_outside_1_to_168_hours_is_refused(int hours) =>
        Should.Throw<InvalidOperationException>(() => new NotificationStatusSettings(hours).Validate())
            .Message.ShouldBe("The Notifications:Status:RetentionHours setting must be from one to one hundred sixty-eight hours.");

    private sealed class RecordingCache : IDistributedCache
    {
        public string? LastKey { get; private set; }
        public DistributedCacheEntryOptions? LastOptions { get; private set; }
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { LastKey = key; LastOptions = options; }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        { Set(key, value, options); return Task.CompletedTask; }
    }

    private sealed class BrokenCache : IDistributedCache
    {
        private static InvalidOperationException Down() => new("redis-down:6379,password=secret");
        public byte[]? Get(string key) => throw Down();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Down();
        public void Refresh(string key) => throw Down();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw Down();
        public void Remove(string key) => throw Down();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw Down();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Down();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Down();
    }
}
```
(If `TestLogCapture` cannot be passed to `new LoggerFactory([...])` because of its constructor shape, build the logger the way `SendServiceHost` does: `new ServiceCollection().AddLogging(l => l.AddProvider(_logs))`.)

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test ApiEndpoints/DKNet.Notification.App.Tests --filter "FullyQualifiedName~NotificationStatusStoreTests"`
Expected: compile errors for the missing types.

- [ ] **Step 3: Implement**

`NotificationOutcome.cs`:
```csharp
namespace DKNet.Notification.AppServices.Notifications;

/// <summary>What a caller can learn about its notification: not ended yet, delivered, or not delivered.</summary>
public enum NotificationOutcome
{
    /// <summary>Queued, being delivered, or waiting for its next attempt.</summary>
    Pending,

    /// <summary>The provider accepted the message.</summary>
    Success,

    /// <summary>Not delivered: failed, or skipped. The reason is never shown.</summary>
    Failed
}
```

`NotificationStatusRecord.cs`:
```csharp
namespace DKNet.Notification.AppServices.Notifications;

/// <summary>The status record of one notification. It holds no personal data.</summary>
/// <param name="NotificationId">The id the caller got back.</param>
/// <param name="IdempotencyKey">The <c>Idempotency-Key</c> of the accepting call.</param>
/// <param name="Status">Where the notification stands.</param>
public sealed record NotificationStatusRecord(Guid NotificationId, string? IdempotencyKey, NotificationOutcome Status);
```

`NotificationStatusSettings.cs` (mirror `DeliverySettings`):
```csharp
namespace DKNet.Notification.AppServices.Notifications;

/// <summary>How long a status record is kept (spec §7).</summary>
/// <param name="retentionHours">Hours from the last write: 1 to 168.</param>
public sealed class NotificationStatusSettings(int retentionHours = 24)
{
    #region Fields

    /// <summary>The configuration section of these settings.</summary>
    public const string SectionName = "Notifications:Status";

    #endregion

    #region Properties

    /// <summary>Gets the hours a status record is kept after its last write.</summary>
    public int RetentionHours { get; } = retentionHours;

    #endregion

    #region Methods

    /// <summary>Refuses a value outside its rule, naming the setting and never its value.</summary>
    public void Validate()
    {
        if (RetentionHours is < 1 or > 168)
        {
            throw new InvalidOperationException(
                $"The {SectionName}:{nameof(RetentionHours)} setting must be from one to one hundred sixty-eight hours.");
        }
    }

    #endregion
}
```

`NotificationStatusStore.cs`:
```csharp
using System.Text.Json;
using DKNet.Notification.Share.Extensions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace DKNet.Notification.AppServices.Notifications;

/// <summary>
///     The status records (spec §7), over the shared <see cref="IDistributedCache" />: Redis, or memory in local runs and
///     tests. A record is keyed by its caller, so a caller only ever reads its own.
/// </summary>
public sealed class NotificationStatusStore(
    IDistributedCache cache,
    NotificationStatusSettings settings,
    ILogger<NotificationStatusStore> logger)
{
    #region Methods

    /// <summary>The cache key of a caller's notification.</summary>
    /// <param name="callerId">The calling application's id.</param>
    /// <param name="notificationId">The notification id.</param>
    /// <returns><c>status:{callerId}:{notificationId}</c>.</returns>
    public static string KeyOf(string callerId, Guid notificationId) => $"status:{callerId}:{notificationId}";

    /// <summary>
    ///     Writes a status, kept for the retention time from now. A write that fails is logged and dropped: it never
    ///     fails the call or the delivery.
    /// </summary>
    /// <param name="callerId">The calling application's id.</param>
    /// <param name="record">The status to keep.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task WriteAsync(string callerId, NotificationStatusRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callerId);
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            await cache.SetAsync(
                KeyOf(callerId, record.NotificationId),
                JsonSerializer.SerializeToUtf8Bytes(record),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(settings.RetentionHours) },
                cancellationToken);
        }
#pragma warning disable CA1031 // A status write is best effort (spec §7); the error text may hold a connection string.
        catch (Exception)
#pragma warning restore CA1031
        {
            logger.NotificationStatusWriteFailed(record.NotificationId, record.Status, callerId.SanitizeForLogging());
        }
    }

    /// <summary>Reads a caller's status; <see langword="null" /> when there is none, or it is another caller's, or it expired.</summary>
    /// <param name="callerId">The calling application's id.</param>
    /// <param name="notificationId">The notification id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The record, or <see langword="null" />.</returns>
    public async Task<NotificationStatusRecord?> ReadAsync(string callerId, Guid notificationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callerId);
        var bytes = await cache.GetAsync(KeyOf(callerId, notificationId), cancellationToken);
        return bytes is null ? null : JsonSerializer.Deserialize<NotificationStatusRecord>(bytes);
    }

    #endregion
}
```
(If the analyzer set flags the `catch (Exception)` with a rule other than CA1031, suppress that rule the same way with the same justification. If `SanitizeForLogging` lives in a namespace other than `DKNet.Notification.Share.Extensions`, use the one `SendNotificationService` imports.)

`NotificationLog.cs`, after the 2006 entry:
```csharp
    /// <remarks>Ids and the status only: the error is not logged, as its text may hold a connection string.</remarks>
    [LoggerMessage(
        EventId = 2007,
        EventName = "NotificationStatusWriteFailed",
        Level = LogLevel.Warning,
        Message = "The status {Status} of notification {NotificationId} could not be written. Caller {CallerId}.")]
    public static partial void NotificationStatusWriteFailed(
        this ILogger logger,
        Guid notificationId,
        NotificationOutcome status,
        string callerId);
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test ApiEndpoints/DKNet.Notification.App.Tests --filter "FullyQualifiedName~NotificationStatusStoreTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ApiEndpoints/DKNet.Notification.AppServices/Notifications ApiEndpoints/DKNet.Notification.App.Tests/Unit/Notifications/NotificationStatusStoreTests.cs
git commit -m "feat: keep a caller-scoped notification status in the distributed cache"
```

---

### Task 3: The queued message and `DeliveryConsumer`

**Files:**
- Create: `ApiEndpoints/DKNet.Notification.AppServices/Delivery/DeliverNotification.cs`
- Create: `ApiEndpoints/DKNet.Notification.AppServices/Delivery/IDeliveryBacklog.cs`
- Create: `ApiEndpoints/DKNet.Notification.AppServices/Delivery/DeliveryConsumer.cs`
- Modify: `ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationLog.cs` (add 2008)
- Create: `ApiEndpoints/DKNet.Notification.App.Tests/Unit/Delivery/DeliveryConsumerTests.cs`
- Delete (in Task 4, once nothing references them): `DeliveryWorker.cs`, `DeliveryQueue.cs`, `QueuedNotification.cs` and their tests.

**Interfaces:**
- Consumes: `Notification.Resume` (Task 1); `NotificationStatusStore`, `NotificationStatusRecord`, `NotificationOutcome` (Task 2); existing `IDeliverySender`, `DeliveryFailure`, `DeliverySettings`, `NotificationMetrics`, `NotificationLog.NotificationAttemptFailed/NotificationDelivered/NotificationFailed`, `TeamsWebhookSender.ChannelKey`.
- Produces:
  ```csharp
  public sealed record DeliverNotification(int SchemaVersion, Guid NotificationId, string TemplateId, string Channel,
      string CallerId, string? IdempotencyKey, DateTimeOffset AcceptedAt, string TraceId, string? EmailAddress,
      string? TeamsDestination, string Subject, string Body, BodyFormat Format, int AttemptsMade, DateTimeOffset NotBefore)
  { public const int CurrentSchemaVersion = 1; public const string QueueName = "notification-delivery"; }

  public interface IDeliveryBacklog { ValueTask<long> LengthAsync(CancellationToken cancellationToken); }
  public sealed class InProcessDeliveryBacklog : IDeliveryBacklog { /* always 0 */ }

  internal sealed class DeliveryConsumer(IMessageBus bus, IDeliverySender sender, DeliverySettings settings,
      NotificationStatusStore status, NotificationMetrics metrics, TimeProvider time, ILogger<DeliveryConsumer> logger,
      [FromKeyedServices(TeamsWebhookSender.ChannelKey)] IDeliverySender? teams = null) : IConsumer<DeliverNotification>
  { public const string ActivitySourceName = "DKNet.Notification"; public static readonly TimeSpan NotDuePause = TimeSpan.FromSeconds(1); }
  ```
  Log: `NotificationLog.NotificationRequeued(this ILogger, Guid notificationId, int attemptsMade, string reason, string traceId)` — EventId 2008, Debug. `reason` is `"not-due"`, `"retry"` or `"stopping"`.

**Behaviour (spec §6):**
```
OnHandle(message, ct):
  now = time.GetUtcNow()
  if message.NotBefore > now:
      await bus.Publish(message, cancellationToken: CancellationToken.None)          // back to the tail
      log NotificationRequeued(reason "not-due")
      wait = min(message.NotBefore - now, NotDuePause); await Task.Delay(wait, time, CancellationToken.None-or-ct, swallow cancellation)
      return
  notification = Resume(...)   // a message whose recipient cannot be re-created → status failed, NotificationFailed, return
  notification.StartAttempt()
  failure = await channelSender.SendAsync(notification, ct)   // inside the same activity StartActivity as DeliveryWorker
      catch OperationCanceledException when ct.IsCancellationRequested → re-publish message unchanged (AttemptsMade not increased), log "stopping", return
      catch any other exception → failure = UnexpectedError (permanent, empty reply code)
  failure null → status Success, NotificationDelivered + metrics.Delivered(channel, now - AcceptedAt)
  failure.IsTransient && notification.AttemptCount < settings.MaxAttempts →
      log NotificationAttemptFailed; next = message with { AttemptsMade = AttemptCount, NotBefore = time.GetUtcNow() + (failure.RetryAfter ?? RetryDelaysSeconds[AttemptCount-1] s) }
      publish next; log NotificationRequeued(reason "retry")
  otherwise → log NotificationAttemptFailed; status Failed; NotificationFailed + metrics.Failed(channel)
  Every publish in this method uses CancellationToken.None, so a stopping host still puts the message back.
  The consumer never lets an exception escape: SMB's Redis consumer would log it and drop the message.
```

- [ ] **Step 1: Write the failing tests** — `DeliveryConsumerTests.cs`

Use `FakeTimeProvider` (`Microsoft.Extensions.Time.Testing`), `MemoryDistributedCache`, a recording bus and a scripted sender. Calls are direct `OnHandle` calls; no bus is started.

```csharp
using System.Collections.Concurrent;
using DKNet.Notification.App.TestSupport;
using DKNet.Notification.AppServices.Delivery;
using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.Domains.Notifications;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SlimMessageBus;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>Spec §6 "Delivering": one attempt per queued message, then a final status or the message back in the queue.</summary>
public sealed class DeliveryConsumerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly TestLogCapture _logs = new();
    private readonly ServiceProvider _services;
    private readonly RecordingBus _bus = new();
    private readonly NotificationStatusStore _status;

    public DeliveryConsumerTests()
    {
        _services = new ServiceCollection().AddMetrics().AddLogging(l => l.AddProvider(_logs)).BuildServiceProvider();
        _status = new NotificationStatusStore(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            new NotificationStatusSettings(),
            _services.GetRequiredService<ILogger<NotificationStatusStore>>());
    }

    public void Dispose() => _services.Dispose();

    private DeliveryConsumer Consumer(Func<Notification, CancellationToken, Task<DeliveryFailure?>> send, DeliverySettings? settings = null) =>
        new(_bus, new ScriptedSender(send), settings ?? new DeliverySettings(), _status,
            new NotificationMetrics(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            _time, _services.GetRequiredService<ILogger<DeliveryConsumer>>());

    private static DeliverNotification Message(int attemptsMade = 0, DateTimeOffset? notBefore = null) =>
        new(DeliverNotification.CurrentSchemaVersion, Guid.CreateVersion7(), "account-opened", "email", "treasury-ops",
            "order-42", Now.AddSeconds(-1), "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
            "jane@example.com", null, "Your account is open", "Dear Jane", BodyFormat.Html, attemptsMade, notBefore ?? Now);

    private Task<NotificationStatusRecord?> StatusOf(DeliverNotification m) => _status.ReadAsync(m.CallerId, m.NotificationId, CancellationToken.None);

    [Fact]
    public async Task A_delivered_message_ends_success_and_is_not_queued_again()
    {
        var message = Message();
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(null)).OnHandle(message, CancellationToken.None);

        (await StatusOf(message)).ShouldBe(new NotificationStatusRecord(message.NotificationId, "order-42", NotificationOutcome.Success));
        _bus.Published.ShouldBeEmpty();
        _logs.Entries.ShouldContain(e => e.Value("NotificationId") == message.NotificationId.ToString() && e.Message.Contains("delivered", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_permanent_failure_ends_failed_at_once()
    {
        var message = Message();
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(new DeliveryFailure(false, "550"))).OnHandle(message, CancellationToken.None);

        (await StatusOf(message))!.Status.ShouldBe(NotificationOutcome.Failed);
        _bus.Published.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 30)]
    public async Task A_transient_failure_with_attempts_left_goes_back_to_the_queue_after_the_configured_wait(int attemptsMade, int waitSeconds)
    {
        var message = Message(attemptsMade);
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(new DeliveryFailure(true, "421"))).OnHandle(message, CancellationToken.None);

        var next = _bus.Published.ShouldHaveSingleItem();
        next.ShouldBe(message with { AttemptsMade = attemptsMade + 1, NotBefore = Now.AddSeconds(waitSeconds) });
        (await StatusOf(message)).ShouldBeNull(); // no final status written; pending was written at acceptance
    }

    [Fact]
    public async Task A_wait_the_provider_asks_for_replaces_the_configured_one()
    {
        var message = Message();
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(new DeliveryFailure(true, "429", TimeSpan.FromSeconds(2)))).OnHandle(message, CancellationToken.None);

        _bus.Published.ShouldHaveSingleItem().NotBefore.ShouldBe(Now.AddSeconds(2));
    }

    [Fact]
    public async Task A_transient_failure_on_the_last_attempt_ends_failed()
    {
        var message = Message(attemptsMade: 2);
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(new DeliveryFailure(true, "421"))).OnHandle(message, CancellationToken.None);

        (await StatusOf(message))!.Status.ShouldBe(NotificationOutcome.Failed);
        _bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task MaxAttempts_ends_a_transient_failure_sooner()
    {
        var message = Message();
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(new DeliveryFailure(true, "421")), new DeliverySettings(maxAttempts: 1))
            .OnHandle(message, CancellationToken.None);

        (await StatusOf(message))!.Status.ShouldBe(NotificationOutcome.Failed);
        _bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unexpected_error_ends_failed_and_does_not_escape()
    {
        var message = Message();
        await Consumer((_, _) => throw new InvalidOperationException("jane@example.com")).OnHandle(message, CancellationToken.None);

        (await StatusOf(message))!.Status.ShouldBe(NotificationOutcome.Failed);
        _logs.Messages.ShouldAllBe(m => !m.Contains("jane@example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_host_stopping_during_an_attempt_puts_the_message_back_with_its_attempts_unchanged()
    {
        var message = Message(attemptsMade: 1);
        using var stopping = new CancellationTokenSource();
        await Consumer(async (_, ct) => { await stopping.CancelAsync(); ct.ThrowIfCancellationRequested(); return null; })
            .OnHandle(message, stopping.Token);

        _bus.Published.ShouldHaveSingleItem().ShouldBe(message);
        (await StatusOf(message)).ShouldBeNull();
    }

    [Fact]
    public async Task A_message_that_is_not_due_goes_back_unchanged_without_an_attempt()
    {
        var message = Message(attemptsMade: 1, notBefore: Now.AddSeconds(30));
        var attempts = 0;
        var handling = Consumer((_, _) => { attempts++; return Task.FromResult<DeliveryFailure?>(null); }).OnHandle(message, CancellationToken.None);

        // The consumer pauses at most NotDuePause on the fake clock; move it past the pause.
        await UntilAsync(() => _bus.Published.Count == 1);
        _time.Advance(DeliveryConsumer.NotDuePause);
        await handling;

        attempts.ShouldBe(0);
        _bus.Published.ShouldHaveSingleItem().ShouldBe(message);
    }

    [Fact]
    public async Task A_teams_message_goes_to_the_teams_sender()
    {
        var message = Message() with { Channel = "teams", EmailAddress = null, TeamsDestination = "ops-alerts", Format = BodyFormat.Markdown };
        Notification? seen = null;
        var teams = new ScriptedSender((n, _) => { seen = n; return Task.FromResult<DeliveryFailure?>(null); });
        var consumer = new DeliveryConsumer(_bus, new ScriptedSender((_, _) => throw new InvalidOperationException("email sender used")),
            new DeliverySettings(), _status, new NotificationMetrics(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            _time, _services.GetRequiredService<ILogger<DeliveryConsumer>>(), teams);

        await consumer.OnHandle(message, CancellationToken.None);

        seen!.TeamsRecipient!.Name.ShouldBe("ops-alerts");
        (await StatusOf(message))!.Status.ShouldBe(NotificationOutcome.Success);
    }

    [Fact]
    public async Task A_teams_message_with_no_teams_sender_ends_failed()
    {
        var message = Message() with { Channel = "teams", EmailAddress = null, TeamsDestination = "ops-alerts", Format = BodyFormat.Markdown };
        await Consumer((_, _) => Task.FromResult<DeliveryFailure?>(null)).OnHandle(message, CancellationToken.None);

        (await StatusOf(message))!.Status.ShouldBe(NotificationOutcome.Failed);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException("The condition was not met within 5 seconds.");
            await Task.Delay(10);
        }
    }

    private sealed class ScriptedSender(Func<Notification, CancellationToken, Task<DeliveryFailure?>> send) : IDeliverySender
    {
        public Task<DeliveryFailure?> SendAsync(Notification notification, CancellationToken stoppingToken) => send(notification, stoppingToken);
    }

    private sealed class RecordingBus : IMessageBus
    {
        private readonly ConcurrentQueue<DeliverNotification> _published = new();
        public IReadOnlyCollection<DeliverNotification> Published => _published.ToArray();

        public Task Publish<TMessage>(TMessage message, string? path = null, IDictionary<string, object>? headers = null, CancellationToken cancellationToken = default)
        {
            _published.Enqueue((DeliverNotification)(object)message!);
            return Task.CompletedTask;
        }

        // The rest of IMessageBus is not used by the consumer.
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, string? path = null, IDictionary<string, object>? headers = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send(IRequest request, string? path = null, IDictionary<string, object>? headers = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResponse> Send<TResponse, TRequest>(TRequest request, string? path = null, IDictionary<string, object>? headers = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
```
`IMessageBus` in SMB 3.5.0 may declare a different set of `Send` overloads: implement exactly the members the compiler asks for, each `throw new NotSupportedException()`. The log assertion in the first test is a guide — match on `EventName == "NotificationDelivered"` if `CapturedLogEntry` exposes it.

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test ApiEndpoints/DKNet.Notification.App.Tests --filter "FullyQualifiedName~DeliveryConsumerTests"`
Expected: compile errors for the missing types.

- [ ] **Step 3: Implement**

`DeliverNotification.cs`:
```csharp
using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     A rendered notification in the delivery queue (spec §5). Fields are only ever added, as optional: a message queued
///     by one release must still be read by the next. The recipient and the body are personal data and never logged.
/// </summary>
/// <param name="SchemaVersion">The shape of this message: <see cref="CurrentSchemaVersion" />.</param>
/// <param name="NotificationId">The id the caller got back.</param>
/// <param name="TemplateId">The template id the caller named.</param>
/// <param name="Channel">The channel in lower case.</param>
/// <param name="CallerId">The calling application's id.</param>
/// <param name="IdempotencyKey">The <c>Idempotency-Key</c> of the accepting call.</param>
/// <param name="AcceptedAt">When the call was accepted.</param>
/// <param name="TraceId">The accepting call's trace id, which every delivery log entry names.</param>
/// <param name="EmailAddress">The one address an email goes to; <see langword="null" /> for Teams.</param>
/// <param name="TeamsDestination">The Teams destination name, never its webhook URL; <see langword="null" /> for email.</param>
/// <param name="Subject">The filled subject (email) or title (Teams).</param>
/// <param name="Body">The filled body.</param>
/// <param name="Format">The body format.</param>
/// <param name="AttemptsMade">Delivery attempts already made.</param>
/// <param name="NotBefore">The next attempt starts no sooner than this.</param>
public sealed record DeliverNotification(
    int SchemaVersion,
    Guid NotificationId,
    string TemplateId,
    string Channel,
    string CallerId,
    string? IdempotencyKey,
    DateTimeOffset AcceptedAt,
    string TraceId,
    string? EmailAddress,
    string? TeamsDestination,
    string Subject,
    string Body,
    BodyFormat Format,
    int AttemptsMade,
    DateTimeOffset NotBefore)
{
    #region Fields

    /// <summary>The shape this release writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The delivery queue: a Redis list, or a memory topic in local runs and tests.</summary>
    public const string QueueName = "notification-delivery";

    #endregion
}
```

`IDeliveryBacklog.cs`:
```csharp
namespace DKNet.Notification.AppServices.Delivery;

/// <summary>How many notifications wait in the delivery queue, for the whole service (spec D7).</summary>
public interface IDeliveryBacklog
{
    /// <summary>Counts the notifications waiting in the queue. Approximate: replicas publish at the same time.</summary>
    /// <param name="cancellationToken">Cancels the count.</param>
    /// <returns>The number waiting.</returns>
    ValueTask<long> LengthAsync(CancellationToken cancellationToken);
}

/// <summary>The memory delivery bus of local runs and tests has no list to count, so the queue never fills.</summary>
public sealed class InProcessDeliveryBacklog : IDeliveryBacklog
{
    /// <inheritdoc />
    public ValueTask<long> LengthAsync(CancellationToken cancellationToken) => ValueTask.FromResult(0L);
}
```

`DeliveryConsumer.cs` — port the body of `DeliveryWorker.AttemptAsync`, `Deliver`, `Fail` and `StartActivity` (keep the activity source name `"DKNet.Notification"`, the activity name `"DeliverNotification"`, its tags and the link to the accepting trace, and the exact log calls and metrics). Skeleton:
```csharp
using System.Diagnostics;
using DKNet.Notification.AppServices.Notifications;
using DKNet.Notification.Domains.Notifications;
using DKNet.Notification.Share.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SlimMessageBus;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     Delivers one queued notification per message (spec §6): one attempt, then a final status, or the message back in
///     the queue for its next attempt. No exception leaves it: the Redis consumer would drop the message.
/// </summary>
internal sealed class DeliveryConsumer(
    IMessageBus bus,
    IDeliverySender sender,
    DeliverySettings settings,
    NotificationStatusStore status,
    NotificationMetrics metrics,
    TimeProvider time,
    ILogger<DeliveryConsumer> logger,
    [FromKeyedServices(TeamsWebhookSender.ChannelKey)] IDeliverySender? teams = null) : IConsumer<DeliverNotification>
{
    #region Fields

    /// <summary>The activity source of delivery attempts.</summary>
    public const string ActivitySourceName = "DKNet.Notification";

    /// <summary>The longest a not-yet-due message holds the consumer before it takes the next one.</summary>
    public static readonly TimeSpan NotDuePause = TimeSpan.FromSeconds(1);

    private static readonly ActivitySource Source = new(ActivitySourceName);
    private static readonly DeliveryFailure UnexpectedError = new(IsTransient: false, ReplyCode: string.Empty);

    #endregion

    #region Methods

    public async Task OnHandle(DeliverNotification message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var now = time.GetUtcNow();
        if (message.NotBefore > now)
        {
            await RequeueAsync(message, "not-due");
            await PauseAsync(message.NotBefore - now < NotDuePause ? message.NotBefore - now : NotDuePause, cancellationToken);
            return;
        }

        if (!TryResume(message, out var notification))
        {
            await EndAsync(message, notification: null, NotificationOutcome.Failed, string.Empty);
            return;
        }

        notification.StartAttempt();
        DeliveryFailure? failure;
        using (StartActivity(message, notification.AttemptCount))
        {
            try
            {
                var channelSender = string.Equals(notification.Channel, TeamsWebhookSender.ChannelKey, StringComparison.Ordinal) ? teams : sender;
                failure = channelSender is null ? UnexpectedError : await channelSender.SendAsync(notification, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The host stops: the cut-off attempt is not counted (spec §6).
                await RequeueAsync(message, "stopping");
                return;
            }
#pragma warning disable CA1031 // An unexpected error ends this notification only; its text may hold the recipient.
            catch (Exception)
#pragma warning restore CA1031
            {
                failure = UnexpectedError;
            }
        }

        if (failure is null)
        {
            await EndAsync(message, notification, NotificationOutcome.Success, string.Empty);
            return;
        }

        logger.NotificationAttemptFailed(/* same arguments as DeliveryWorker, with message.TraceId */);
        if (failure.IsTransient && notification.AttemptCount < settings.MaxAttempts)
        {
            // Counted from the end of this attempt: now. A wait the provider asked for replaces the configured one.
            var wait = failure.RetryAfter ?? TimeSpan.FromSeconds(settings.RetryDelaysSeconds[notification.AttemptCount - 1]);
            await RequeueAsync(message with { AttemptsMade = notification.AttemptCount, NotBefore = time.GetUtcNow() + wait }, "retry");
            return;
        }

        await EndAsync(message, notification, NotificationOutcome.Failed, failure.ReplyCode);
    }

    // Always CancellationToken.None: a stopping host must still put the message back (SMB publishes until it is disposed).
    private async Task RequeueAsync(DeliverNotification message, string reason)
    {
        await bus.Publish(message, cancellationToken: CancellationToken.None);
        logger.NotificationRequeued(message.NotificationId, message.AttemptsMade, reason, message.TraceId);
    }

    private async Task PauseAsync(TimeSpan pause, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(pause, time, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The host stops during the pause: the message is already back in the queue.
        }
    }

    private static bool TryResume(DeliverNotification message, out Notification notification) { /* build EmailRecipient or TeamsRecipient with TryCreate; RenderedMessage(message.Subject, message.Body, message.Format); Notification.Resume(...). Return false when the recipient does not re-create or AttemptsMade is out of range (catch ArgumentException). */ }

    private async Task EndAsync(DeliverNotification message, Notification? notification, NotificationOutcome outcome, string replyCode)
    {
        // Success: notification.Deliver(); NotificationDelivered + metrics.Delivered(message.Channel, time.GetUtcNow() - message.AcceptedAt).
        // Failed: notification?.Fail(); NotificationFailed (AttemptCount = notification?.AttemptCount ?? message.AttemptsMade) + metrics.Failed(message.Channel).
        await status.WriteAsync(message.CallerId, new NotificationStatusRecord(message.NotificationId, message.IdempotencyKey, outcome), CancellationToken.None);
    }

    private static Activity? StartActivity(DeliverNotification message, int attempt) { /* as DeliveryWorker.StartActivity, from message.TraceId / message.NotificationId */ }

    #endregion
}
```
Fill the commented bodies from `DeliveryWorker.cs` (lines 64–160 at the time of writing); they move, they do not change. `TryResume`'s `out` parameter needs `[NotNullWhen(true)] out Notification? notification` to satisfy nullable analysis.

`NotificationLog.cs`, after 2007:
```csharp
    [LoggerMessage(
        EventId = 2008,
        EventName = "NotificationRequeued",
        Level = LogLevel.Debug,
        Message = "Notification {NotificationId} put back in the delivery queue ({Reason}) after {AttemptsMade} attempts. Trace {TraceId}.")]
    public static partial void NotificationRequeued(this ILogger logger, Guid notificationId, int attemptsMade, string reason, string traceId);
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test ApiEndpoints/DKNet.Notification.App.Tests --filter "FullyQualifiedName~DeliveryConsumerTests"`
Expected: PASS. (`DeliveryWorker` still exists and still builds at this point.)

- [ ] **Step 5: Commit**

```bash
git add ApiEndpoints/DKNet.Notification.AppServices/Delivery ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationLog.cs ApiEndpoints/DKNet.Notification.App.Tests/Unit/Delivery/DeliveryConsumerTests.cs
git commit -m "feat: deliver one queued notification per message and requeue its retries"
```

---

### Task 4: Accept through the status store and the delivery bus

**Files:**
- Modify: `ApiEndpoints/DKNet.Notification.AppServices/Notifications/SendNotification.cs`
- Modify: `ApiEndpoints/DKNet.Notification.AppServices/Notifications/SendNotificationService.cs`
- Delete: `ApiEndpoints/DKNet.Notification.AppServices/Delivery/DeliveryWorker.cs`, `DeliveryQueue.cs`, `QueuedNotification.cs`
- Delete: `ApiEndpoints/DKNet.Notification.App.Tests/Unit/Delivery/DeliveryWorkerTests.cs`, `DeliveryQueueTests.cs`
- Modify: `ApiEndpoints/DKNet.Notification.App.Tests/Unit/Notifications/SendServiceHost.cs`, `SendNotificationServiceTests.cs`, `SendEmailServiceTests.cs`, `SendTeamsServiceTests.cs`, and any other test that references `DeliveryQueue`/`DeliveryWorker` (`grep -rn "DeliveryQueue\|DeliveryWorker\|QueuedNotification" ApiEndpoints --include=*.cs`).
- Temporarily modify: `ApiEndpoints/DKNet.Notification.Api/Configs/EmailConfig.cs`, `LogConfigs.cs`, delete `DeliveryWorkerHost.cs` — only what is needed to compile; Task 5 finishes the wiring.

**Interfaces:**
- Consumes: Tasks 2 and 3.
- Produces:
  ```csharp
  public sealed record SendNotification(SendNotificationRequest Request, string CallerId, string TraceId, string? IdempotencyKey = null)
      : Fluents.Requests.IWitResponse<Guid>;
  public sealed class SendNotificationService(ITemplateCatalogue catalogue, EmailChannelSettings email, TeamsChannelSettings teams,
      DeliverySettings delivery, IDeliveryBacklog backlog, IMessageBus bus, NotificationStatusStore status,
      NotificationMetrics metrics, TimeProvider time, ILogger<SendNotificationService> logger)
  {
      public Task<Notification> SendAsync(SendNotificationRequest request, string callerId, string traceId, string? idempotencyKey, CancellationToken cancellationToken);
      public void RejectInvalid(string callerId, string traceId); // unchanged
  }
  ```
  The constructor registers the queue-length gauge: `metrics.ObserveQueueLength(() => (int)backlog.LengthAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult());` with a comment `// ponytail: a blocking LLEN per scrape; cache the last count if scrapes become frequent.` — remove the old registration from `DeliveryQueue` (deleted).

**Behaviour (spec §6 "Accepting"):** steps 4–8 unchanged. Step 9 becomes:
```
if await backlog.LengthAsync(ct) >= delivery.QueueCapacity → Reject(QUEUE_FULL) (nothing written)
notification.Queue(recipient, renderedMessage)                     // domain transition, as DeliveryQueue.TryEnqueue did
await status.WriteAsync(callerId, new(notification.NotificationId, idempotencyKey, Pending), ct)   // BEFORE publish
await bus.Publish(new DeliverNotification(CurrentSchemaVersion, id, templateId, channel, callerId, idempotencyKey,
    acceptedAt, traceId, email?.Address, teams?.Name, rendered.Subject, rendered.Body, rendered.Format, 0, time.GetUtcNow()), cancellationToken: ct)
log NotificationQueued(id, queueLength: (int)(length + 1), …)       // the length read before publishing, plus this one
metrics.Accepted(channel, "queued")
```
Skip(...) additionally writes `new(id, idempotencyKey, Failed)`. Reject writes nothing. A publish that throws is not caught (the call answers 500).

- [ ] **Step 1: Rework `SendServiceHost`** so it builds the service with `InProcessDeliveryBacklog` or a settable fake backlog, a `RecordingBus` (move the one from `DeliveryConsumerTests` to `ApiEndpoints/DKNet.Notification.App.Tests/Unit/Delivery/RecordingBus.cs` as `internal sealed`, and make `DeliveryConsumerTests` use it), a `MemoryDistributedCache`-backed `NotificationStatusStore`, and expose `Bus`, `Status` and a `Backlog` whose length a test can set:
```csharp
internal sealed class FixedBacklog : IDeliveryBacklog
{
    public long Length { get; set; }
    public ValueTask<long> LengthAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Length);
}
```
Replace `int queueCapacity = 10` with `DeliverySettings? delivery = null` and register `delivery ?? new DeliverySettings(queueCapacity: 10)`.

- [ ] **Step 2: Write the failing tests** (add to `SendNotificationServiceTests.cs`; reuse its existing template/settings builders)

```csharp
    [Fact]
    public async Task A_queued_call_writes_pending_before_it_publishes_and_carries_the_idempotency_key()
    {
        using var host = new SendServiceHost();
        var service = host.Service(Catalogue(), EmailOn(), TeamsOff());
        NotificationStatusRecord? seenAtPublish = null;
        host.Bus.OnPublish = m => seenAtPublish = host.Status.ReadAsync("treasury-ops", m.NotificationId, CancellationToken.None).GetAwaiter().GetResult();

        var notification = await service.SendAsync(EmailRequest("jane@example.com"), "treasury-ops", TraceId, "order-42", CancellationToken.None);

        notification.Status.ShouldBe(NotificationStatus.Queued);
        seenAtPublish.ShouldBe(new NotificationStatusRecord(notification.NotificationId, "order-42", NotificationOutcome.Pending));
        var message = host.Bus.Published.ShouldHaveSingleItem();
        message.EmailAddress.ShouldBe("jane@example.com");
        message.AttemptsMade.ShouldBe(0);
        message.IdempotencyKey.ShouldBe("order-42");
    }

    [Fact]
    public async Task A_skipped_call_is_failed_at_once_and_publishes_nothing()
    {
        using var host = new SendServiceHost();
        var service = host.Service(Catalogue(), EmailOff(), TeamsOff());

        var notification = await service.SendAsync(EmailRequest("jane@example.com"), "treasury-ops", TraceId, "order-42", CancellationToken.None);

        notification.Status.ShouldBe(NotificationStatus.Skipped);
        (await host.Status.ReadAsync("treasury-ops", notification.NotificationId, CancellationToken.None))!.Status.ShouldBe(NotificationOutcome.Failed);
        host.Bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rejected_call_writes_no_status()
    {
        using var host = new SendServiceHost();
        var service = host.Service(Catalogue(), EmailOn(), TeamsOff());

        var notification = await service.SendAsync(EmailRequest("not an address"), "treasury-ops", TraceId, "order-42", CancellationToken.None);

        notification.Status.ShouldBe(NotificationStatus.Rejected);
        (await host.Status.ReadAsync("treasury-ops", notification.NotificationId, CancellationToken.None)).ShouldBeNull();
        host.Bus.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_full_backlog_refuses_the_call_and_writes_nothing()
    {
        using var host = new SendServiceHost();
        var service = host.Service(Catalogue(), EmailOn(), TeamsOff(), new DeliverySettings(queueCapacity: 2));
        host.Backlog.Length = 2;

        var notification = await service.SendAsync(EmailRequest("jane@example.com"), "treasury-ops", TraceId, "order-42", CancellationToken.None);

        notification.ErrorCode.ShouldBe(NotificationErrorCodes.QueueFull);
        (await host.Status.ReadAsync("treasury-ops", notification.NotificationId, CancellationToken.None)).ShouldBeNull();
        host.Bus.Published.ShouldBeEmpty();
    }
```
(`Catalogue()`, `EmailOn()`, `EmailOff()`, `TeamsOff()`, `EmailRequest(...)`, `TraceId` stand for the builders the test class already has; use their real names. Add `public Action<DeliverNotification>? OnPublish { get; set; }` to `RecordingBus`, invoked before it records.)

- [ ] **Step 3: Run to see them fail**

Run: `dotnet test ApiEndpoints/DKNet.Notification.App.Tests --filter "FullyQualifiedName~SendNotificationServiceTests"`
Expected: compile errors (`SendAsync`, `host.Bus`).

- [ ] **Step 4: Implement**

- `SendNotification`: add `string? IdempotencyKey = null` as the last positional parameter, documented "The <c>Idempotency-Key</c> header of the call, kept in its status record." The handler becomes:
  ```csharp
  public async Task<IResult<Guid>> OnHandle(SendNotification request, CancellationToken cancellationToken)
  {
      ArgumentNullException.ThrowIfNull(request);
      var notification = await service.SendAsync(request.Request, request.CallerId, request.TraceId, request.IdempotencyKey, cancellationToken);
      return notification.Status == NotificationStatus.Rejected
          ? Result.Fail<Guid>(/* unchanged */)
          : Result.Ok(notification.NotificationId);
  }
  ```
- `SendNotificationService`: apply the behaviour above. `Send` → `SendAsync`; `SendEmail`/`SendTeams`/`Skip` become async (`Task<Notification>`); `Queued(notification, bool, traceId)` becomes `QueueAsync(notification, EmailRecipient? email, TeamsRecipient? teams, RenderedMessage rendered, string traceId, string? idempotencyKey, CancellationToken ct)`. Update the class XML doc: "Steps 4 to 9 of a send call … step 9 writes the pending status, then puts the notification in the delivery queue."
- Delete `DeliveryWorker.cs`, `DeliveryQueue.cs`, `QueuedNotification.cs`, `DeliveryWorkerTests.cs`, `DeliveryQueueTests.cs`, `Api/Configs/DeliveryWorkerHost.cs`.
- To compile the Api for now: in `EmailConfig.cs` remove `.AddSingleton<DeliveryQueue>().AddSingleton<DeliveryWorker>().AddHostedService<DeliveryWorkerHost>()`; in `LogConfigs.cs` use `DeliveryConsumer.ActivitySourceName` — `DeliveryConsumer` is internal to AppServices, so use the literal through a new `public const string DeliveryActivitySourceName = "DKNet.Notification";` on `NotificationMetrics` (same value as `MeterName`) and point both `DeliveryConsumer.ActivitySourceName` and `LogConfigs` at it.
- Port each assertion of the deleted `DeliveryWorkerTests` that is not yet covered by `DeliveryConsumerTests` (log field contents of `NotificationAttemptFailed`/`NotificationDelivered`/`NotificationFailed`, the metrics `notifications.delivered`, `notifications.delivery.duration` (unit `s`), `notifications.failed`, the activity tags and link) into `DeliveryConsumerTests` — those are behaviour the operator relies on. Drop assertions about `queue.Length`, places and in-memory timers.
- Update `SendEmailServiceTests`, `SendTeamsServiceTests` and the rest of `SendNotificationServiceTests` to `await service.SendAsync(..., idempotencyKey: null, CancellationToken.None)`. A test that asserted `queue.Length` asserts `host.Bus.Published.Count` instead; a test that filled the queue sets `host.Backlog.Length`.

- [ ] **Step 5: Run the whole unit suite**

Run: `dotnet build -c Release && dotnet test ApiEndpoints/DKNet.Notification.App.Tests`
Expected: PASS except tests that start the full host and need Task 5's wiring (they fail with a DI error for `IDeliveryBacklog`/`IMessageBus` publish). List them in the task report; Task 5 makes them pass.

- [ ] **Step 6: Commit**

```bash
git add -A ApiEndpoints
git commit -m "feat: accept a notification through its pending status and the delivery bus"
```

---

### Task 5: Bus and service wiring

**Files:**
- Modify: `ApiEndpoints/DKNet.Notification.Api/Configs/ServiceConfigs.cs`
- Create: `ApiEndpoints/DKNet.Notification.Api/Configs/RedisDeliveryBacklog.cs`
- Modify: `ApiEndpoints/DKNet.Notification.Api/Configs/EmailConfig.cs` (bind and validate `NotificationStatusSettings` like `DeliverySettings`; register `NotificationStatusStore`)
- Modify: `ApiEndpoints/DKNet.Notification.Api/Program.cs` or `AppConfig.cs` only if `AddAllAppServices` needs `IConfiguration`/`IHostEnvironment` passed in.
- Modify: `ApiEndpoints/DKNet.Notification.Api/appsettings.json` (add `"Status": { "RetentionHours": 24 }` under `Notifications`)
- Test: `ApiEndpoints/DKNet.Notification.App.Tests/Integration/Notifications/BadDeliverySettingStartupTests.cs` (add the status rule)

**Interfaces:**
- Consumes: everything above.
- Produces: a running host where `IMessageBus` routes `SendNotification` and `GetNotificationStatus` (Task 6) to the `Mediator` child bus and `DeliverNotification` to the `Delivery` child bus.

- [ ] **Step 1: Write the failing startup test** (in `BadDeliverySettingStartupTests.cs`, same pattern as its existing cases)

```csharp
    [Theory]
    [InlineData("0")]
    [InlineData("169")]
    public void A_status_retention_outside_its_rule_stops_the_host(string hours) =>
        StartWith("Notifications:Status:RetentionHours", hours)   // use the class's existing start helper
            .Message.ShouldBe("The Notifications:Status:RetentionHours setting must be from one to one hundred sixty-eight hours.");
```

- [ ] **Step 2: Run to see it fail**

Run: `dotnet test ApiEndpoints/DKNet.Notification.App.Tests --filter "FullyQualifiedName~BadDeliverySettingStartupTests"`
Expected: FAIL (host starts).

- [ ] **Step 3: Implement**

`RedisDeliveryBacklog.cs`:
```csharp
using DKNet.Notification.AppServices.Delivery;
using StackExchange.Redis;

namespace DKNet.Notification.Api.Configs;

/// <summary>Counts the Redis delivery list (spec D7): <c>LLEN notification-delivery</c>.</summary>
[ExcludeFromCodeCoverage]
internal sealed class RedisDeliveryBacklog(IConnectionMultiplexer redis) : IDeliveryBacklog
{
    public async ValueTask<long> LengthAsync(CancellationToken cancellationToken) =>
        await redis.GetDatabase().ListLengthAsync(DeliverNotification.QueueName);
}
```

`ServiceConfigs.AddAllAppServices` — give it the Redis connection string (read in `AppConfig` the same way idempotency reads it: `configuration.GetConnectionString(SharedConsts.RedisConnectionString)`), then:
```csharp
    public static IServiceCollection AddAllAppServices(this IServiceCollection services, string? redisConnectionString)
    {
        services
            .AddSingleton(TimeProvider.System)
            .AddSingleton<NotificationMetrics>()
            .AddSingleton<SendNotificationService>();

        IConnectionMultiplexer? redis = null;
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            // Local runs and tests only: AppConfig already refuses to start without Redis anywhere else.
            services.AddSingleton<IDeliveryBacklog, InProcessDeliveryBacklog>();
        }
        else
        {
            redis = ConnectionMultiplexer.Connect(redisConnectionString);
            services.AddSingleton(redis).AddSingleton<IDeliveryBacklog, RedisDeliveryBacklog>();
        }

        var assembly = typeof(SendNotification).Assembly;
        return services.AddSlimMessageBus(mbb => mbb
            // The mediator from endpoint to handler (ADR-0011): every request except delivery.
            .AddChildBus("Mediator", child => child
                .WithProviderMemory(cf =>
                {
                    cf.EnableMessageHeaders = false;
                    cf.EnableMessageSerialization = false;
                })
                .AutoDeclareFrom(assembly, consumerTypeFilter: t => t.Namespace != typeof(DeliverNotification).Namespace))
            // The delivery queue (ADR-0013): a Redis list, or a non-blocking memory topic in local runs and tests.
            .AddChildBus("Delivery", child =>
            {
                if (redis is null)
                {
                    child.WithProviderMemory(cf =>
                        {
                            cf.EnableBlockingPublish = false;
                            cf.EnableMessageSerialization = false;
                        })
                        .Produce<DeliverNotification>(x => x.DefaultTopic(DeliverNotification.QueueName))
                        .Consume<DeliverNotification>(x => x.Topic(DeliverNotification.QueueName).WithConsumer<IConsumer<DeliverNotification>>().Instances(1));
                }
                else
                {
                    child.WithProviderRedis(cfg => cfg.ConnectionFactory = () => redis)
                        .AddJsonSerializer()
                        .Produce<DeliverNotification>(x => x.DefaultQueue(DeliverNotification.QueueName))
                        .Consume<DeliverNotification>(x => x.Queue(DeliverNotification.QueueName).WithConsumer<IConsumer<DeliverNotification>>().Instances(1));
                }
            })
            .AddServicesFromAssembly(assembly));
    }
```
Notes for the implementer:
- `DeliveryConsumer` is `internal`; `AddServicesFromAssembly` registers it as `IConsumer<DeliverNotification>`, which is why the consumer is named by its interface. If SMB requires a concrete type in `WithConsumer`, add `[assembly: InternalsVisibleTo("DKNet.Notification.Api")]` to AppServices' csproj (`<InternalsVisibleTo Include="DKNet.Notification.Api"/>`) and use `WithConsumer<DeliveryConsumer>()`, and say so in the report.
- `AutoDeclareFrom` with the namespace filter keeps `DeliveryConsumer` out of the mediator bus; otherwise the hybrid bus would route `DeliverNotification` to both child buses.
- Check the exact builder method names against SMB 3.5.0 (`DefaultTopic`, `Topic`, `DefaultQueue`, `Queue`, `AddJsonSerializer` from `SlimMessageBus.Host.Serialization.SystemTextJson`). If `AddJsonSerializer` must sit on the root builder, put it on `mbb` — the memory bus ignores a serializer when `EnableMessageSerialization` is false.
- Update `AppConfig.AddAppConfig` to call `services.AddAllAppServices(redisConnectionString)` (it already reads the connection string a few lines above for idempotency — move the read up and reuse it).
- `EmailConfig`: bind `NotificationStatusSettings` exactly as `BindDelivery` binds `DeliverySettings` (including the unconvertible-value refusal), call `Validate()`, register it and `.AddSingleton<NotificationStatusStore>()`.
- The mail-catcher / Graph / Teams senders keep their registrations.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet build -c Release && dotnet test --settings coverage.runsettings`
Expected: App.Tests PASS. BDD: scenarios that assert `202` fail (fixed in Task 6/7); note any other failure in the task report.

- [ ] **Step 5: Commit**

```bash
git add -A ApiEndpoints
git commit -m "feat: route delivery through the SlimMessageBus Redis queue, memory in local runs"
```

---

### Task 6: Endpoints — POST 200 and GET status

**Files:**
- Create: `ApiEndpoints/DKNet.Notification.AppServices/Notifications/GetNotificationStatus.cs`
- Modify: `ApiEndpoints/DKNet.Notification.AppServices/Notifications/NotificationErrorCodes.cs`
- Create: `ApiEndpoints/DKNet.Notification.Api/ApiEndpoints/Notifications/NotificationStatusResponse.cs`
- Modify: `ApiEndpoints/DKNet.Notification.Api/ApiEndpoints/Notifications/NotificationsV1Endpoint.cs`
- Modify: `ApiEndpoints/DKNet.Notification.Api/Configs/FluentValidationConfig.cs`
- Test: `ApiEndpoints/DKNet.Notification.App.Tests/Unit/Notifications/NotificationsV1EndpointTests.cs` (and the route tests that boot the host, wherever `POST /v1/notifications` 202 is asserted today)

**Interfaces:**
- Produces:
  ```csharp
  public sealed record GetNotificationStatus(Guid NotificationId, string CallerId) : Fluents.Requests.IWitResponse<NotificationStatusRecord>;
  internal sealed class GetNotificationStatusHandler(NotificationStatusStore status) : Fluents.Requests.IHandler<GetNotificationStatus, NotificationStatusRecord>;
  // fails with Error(NotificationErrorCodes.NotificationNotFound) carrying SendNotification.CodeMetadata / FieldMetadata ("notificationId")
  public const string NotificationNotFound = "NOTIFICATION_NOT_FOUND";   // NotificationErrorCodes
  public sealed record NotificationStatusResponse(Guid NotificationId, string? IdempotencyKey, string Status); // Status: "pending" | "success" | "failed"
  ```

- [ ] **Step 1: Write the failing route tests** (in the class that boots the API with `TestApiFactoryBase` and posts to `/v1/notifications` today — find it with `grep -rln "v1/notifications" ApiEndpoints/DKNet.Notification.App.Tests`). Use its existing helpers for sign-in as a caller and for a valid body:

```csharp
    [Fact]
    public async Task An_accepted_call_answers_200_with_its_id_and_its_status_reads_pending_or_ended()
    {
        var response = await PostAsync(caller: "treasury-ops", key: "order-42", ValidEmailBody());
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("notificationId").GetGuid();

        var status = await GetAsync(caller: "treasury-ops", $"/v1/notifications/{id}");

        status.StatusCode.ShouldBe(HttpStatusCode.OK);
        status.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var body = await status.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("notificationId").GetGuid().ShouldBe(id);
        body.GetProperty("idempotencyKey").GetString().ShouldBe("order-42");
        body.GetProperty("status").GetString().ShouldBeOneOf("pending", "success", "failed");
    }

    [Fact]
    public async Task Another_callers_id_answers_404_like_an_unknown_one()
    {
        var response = await PostAsync(caller: "treasury-ops", key: "order-43", ValidEmailBody());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("notificationId").GetGuid();

        var other = await GetAsync(caller: "payments", $"/v1/notifications/{id}");
        var unknown = await GetAsync(caller: "treasury-ops", $"/v1/notifications/{Guid.CreateVersion7()}");

        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.Content.ReadAsStringAsync()).ShouldContain("NOTIFICATION_NOT_FOUND");
        (await other.Content.ReadAsStringAsync()).ShouldBe(await unknown.Content.ReadAsStringAsync(), "same body apart from trace id", StringCompareShould.IgnoreCase); // compare errors[] only if traceId differs
    }

    [Fact]
    public async Task An_id_that_is_not_a_guid_answers_404()
    {
        (await GetAsync(caller: "treasury-ops", "/v1/notifications/not-a-guid")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
```
(Compare the two 404 bodies by their `errors` array, not the whole text — `traceId` differs per call. Adjust that assertion accordingly.)

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test ApiEndpoints/DKNet.Notification.App.Tests --filter "FullyQualifiedName~Notifications"`
Expected: FAIL (202, and GET 404/405).

- [ ] **Step 3: Implement**

`NotificationErrorCodes.cs`: add
```csharp
    /// <summary>No status record for this caller and id: unknown, another caller's, or expired.</summary>
    public const string NotificationNotFound = "NOTIFICATION_NOT_FOUND";
```

`GetNotificationStatus.cs`:
```csharp
using DKNet.SlimBus.Extensions;
using FluentResults;

namespace DKNet.Notification.AppServices.Notifications;

/// <summary>A caller asks where its notification stands (spec §8), on the in-memory bus (ADR-0011).</summary>
/// <param name="NotificationId">The id the caller got back.</param>
/// <param name="CallerId">The caller that passed sign-in.</param>
public sealed record GetNotificationStatus(Guid NotificationId, string CallerId) : Fluents.Requests.IWitResponse<NotificationStatusRecord>;

/// <summary>Reads the caller's own status record; fails with <see cref="NotificationErrorCodes.NotificationNotFound" /> otherwise.</summary>
internal sealed class GetNotificationStatusHandler(NotificationStatusStore status)
    : Fluents.Requests.IHandler<GetNotificationStatus, NotificationStatusRecord>
{
    public async Task<IResult<NotificationStatusRecord>> OnHandle(GetNotificationStatus request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var record = await status.ReadAsync(request.CallerId, request.NotificationId, cancellationToken);
        return record is null
            ? Result.Fail<NotificationStatusRecord>(new Error(NotificationErrorCodes.NotificationNotFound)
                .WithMetadata(SendNotification.CodeMetadata, NotificationErrorCodes.NotificationNotFound)
                .WithMetadata(SendNotification.FieldMetadata, "notificationId"))
            : Result.Ok(record);
    }
}
```

`NotificationStatusResponse.cs`:
```csharp
namespace DKNet.Notification.Api.ApiEndpoints.Notifications;

/// <summary>The body of <c>GET /v1/notifications/{notificationId}</c>.</summary>
/// <param name="NotificationId">The id the caller got back.</param>
/// <param name="IdempotencyKey">The <c>Idempotency-Key</c> of the accepting call.</param>
/// <param name="Status"><c>pending</c>, <c>success</c> or <c>failed</c>.</param>
internal sealed record NotificationStatusResponse(Guid NotificationId, string? IdempotencyKey, string Status);
```
(Match the visibility of `SendNotificationResponse`.)

`NotificationsV1Endpoint`:
- `Map`: chain the GET on the same group:
  ```csharp
  public void Map(RouteGroupBuilder group)
  {
      group.MapPost(string.Empty, Send)
          .Accepts<SendNotificationRequest>("application/json")
          .Produces<SendNotificationResponse>(StatusCodes.Status200OK)
          .AddEndpointFilter<SendNotificationBodyFilter>()
          .RequiredIdempotentKey();
      group.MapGet("{notificationId:guid}", Status)
          .Produces<NotificationStatusResponse>(StatusCodes.Status200OK)
          .Produces(StatusCodes.Status404NotFound);
  }
  ```
- `Send`: pass the key — `context.Request.Headers["Idempotency-Key"].ToString()` (empty → `null`) — as `IdempotencyKey`, and answer `TypedResults.Ok(new SendNotificationResponse(result.Value))`. Update the comment "Queued or Skipped: the same answer" to keep saying so.
- New handler:
  ```csharp
  private static async Task<IResult> Status(
      Guid notificationId,
      [FromServices] IMessageBus bus,
      [FromServices] IFluentValidationAutoValidationResultFactory problems,
      HttpContext context)
  {
      context.Response.Headers.CacheControl = "no-store";
      var result = await bus.Send(new GetNotificationStatus(notificationId, CallerOf(context)));
      if (result.IsSuccess)
      {
          var record = result.Value;
          return TypedResults.Ok(new NotificationStatusResponse(record.NotificationId, record.IdempotencyKey,
              record.Status.ToString().ToLowerInvariant()));
      }

      return problems.CreateResult(
          EndpointFilterInvocationContext.Create(context),
          new ValidationResult([new ValidationFailure("notificationId", "No notification with this id.") { ErrorCode = NotificationErrorCodes.NotificationNotFound }]));
  }
  ```
  `ToLowerInvariant` will raise CA1308; suppress it on the method with justification "The public status values are lower case (spec §8)."
- `FluentValidationConfig`: extend the `StatusCode` chain so an error with code `NotificationErrorCodes.NotificationNotFound` answers `StatusCodes.Status404NotFound` (same shape as the `QueueFull` → 503 branch).
- Every existing test and BDD step that expects `202` from `POST /v1/notifications` now expects `200` — `grep -rn "202\|Status202\|Accepted" ApiEndpoints/DKNet.Notification.App.Tests ApiEndpoints/DKNet.Notification.App.BDDTests --include=*.cs --include=*.feature`. Regenerate nothing by hand: Reqnroll regenerates `*.feature.cs` on build.

- [ ] **Step 4: Run the tests**

Run: `dotnet test --settings coverage.runsettings`
Expected: App.Tests PASS; BDD PASS except the queue-full scenarios (Task 7).

- [ ] **Step 5: Commit**

```bash
git add -A ApiEndpoints
git commit -m "feat: answer an accepted notification with 200 and let its caller read its status"
```

---

### Task 7: BDD on real Redis — status, restart and queue-full

**Files:**
- Create: `ApiEndpoints/DKNet.Notification.App.BDDTests/Features/Notifications/NotificationStatus.feature`
- Create: `ApiEndpoints/DKNet.Notification.App.BDDTests/Features/Notifications/Steps/NotificationStatusSteps.cs`
- Modify: `ApiEndpoints/DKNet.Notification.App.BDDTests/Features/Notifications/EmailChannel.feature` (queue-full rule), `TeamsChannel.feature` ("Email and Teams share the places of the queue") and their step classes only as needed
- Modify: `ApiEndpoints/DKNet.Notification.App.BDDTests/Support/RedisServer.cs` only if it does not already clear the delivery list between scenarios

**Interfaces:**
- Consumes: the running API (Tasks 5–6), `SendApiFactory`, `RedisServer` (one container per feature via `FeatureKey`), `MailCatcher`, `ScenarioState`.

- [ ] **Step 1: Make scenario isolation hold for the delivery list.** Read `RedisServer.cs` and `ApiHooks.cs`. A message left in `notification-delivery` by one scenario would be delivered by the next scenario's host. If the feature's Redis is not flushed before each scenario, add a `[BeforeScenario]` step (in the existing hooks, same `Order` convention) that runs `FLUSHDB` on the feature's Redis — or `KeyDeleteAsync("notification-delivery")` plus the `status:*` keys if a full flush would break idempotency scenarios that rely on state within one scenario only.

- [ ] **Step 2: Write the feature**

```gherkin
Feature: Notification status
  A caller reads where its own notification stands: pending, success or failed (spec §7, §8).

  Background:
    Given "treasury-ops" is a caller allowed to send notifications

  @integration
  Scenario: A delivered email reads success
    Given the service runs with sign-in on, Redis and email set up to send to the mail catcher
    When "treasury-ops" emails template "account-opened" to "jane@example.com" with the key "st-1001"
    Then the call is answered 200 with a notification id
    And within 10 seconds the status of that notification reads "success" with the key "st-1001"

  @integration
  Scenario: An email that fails 3 times reads failed
    Given the service runs with sign-in on, Redis, email set up to send to the mail catcher and the delivery waits of 1 second and 1 second
    And the mail catcher is stopped
    When "treasury-ops" emails template "account-opened" to "jane@example.com" with the key "st-1002"
    Then the status of that notification reads "pending"
    And within 15 seconds the status of that notification reads "failed"

  @integration
  Scenario: A skipped call reads failed at once
    Given the service runs with sign-in on, Redis and email not set up
    When "treasury-ops" emails template "account-opened" to "jane@example.com" with the key "st-1003"
    Then the call is answered 200 with a notification id
    And the status of that notification reads "failed"

  @integration
  Scenario: Another caller cannot read the status
    Given the service runs with sign-in on, Redis and email set up to send to the mail catcher
    And "payments" is a caller allowed to send notifications
    When "treasury-ops" emails template "account-opened" to "jane@example.com" with the key "st-1004"
    Then "payments" reading the status of that notification is answered 404 with "NOTIFICATION_NOT_FOUND"

  @integration
  Scenario: A notification waiting for its retry is delivered by the next host after a restart
    Given the service runs with sign-in on, Redis, email set up to send to the mail catcher and the delivery waits of 3 seconds and 30 seconds
    And the mail catcher is stopped
    When "treasury-ops" emails template "account-opened" to "jane@example.com" with the key "st-1005"
    And attempt 1 has failed
    And the service is stopped and started again on the same Redis
    And the mail catcher is started
    Then within 15 seconds Minh can read 1 mail to "jane@example.com" in the mail catcher
    And the status of that notification reads "success"
```
Reuse existing step phrases where they exist (`grep -rn "\[Given\|\[When\|\[Then" Features/Notifications/Steps`) instead of adding near-duplicates; rename the phrases above to match.

- [ ] **Step 3: Implement the new steps** in `NotificationStatusSteps.cs` — GET `/v1/notifications/{id}` as a named caller through the scenario's `HttpClient`/factory (same sign-in helper the send steps use), poll every 200 ms up to the stated time for "within N seconds", and for the restart step dispose the scenario's `SendApiFactory` and build a new one with the same settings and the same Redis connection string, replacing it in `ScenarioState`.

- [ ] **Step 4: Fix the queue-full scenarios** so they run with Redis (the memory fallback has no limit, spec D7): their `Given` must pass the feature's Redis connection. With Redis, a message the consumer is attempting is not in the list for that moment; keep "2 notifications are not delivered yet, because the mail catcher is stopped" — each attempt fails fast and the message waits in the list for its retry, so `LLEN` is 2. If that proves timing-sensitive, set the delivery waits to 30 s and 30 s in that scenario so both messages sit in the list as not-due.

- [ ] **Step 5: Run the BDD suite**

Run: `dotnet test ApiEndpoints/DKNet.Notification.App.BDDTests`
Expected: PASS (Docker running).

- [ ] **Step 6: Commit**

```bash
git add -A ApiEndpoints/DKNet.Notification.App.BDDTests
git commit -m "test: notification status, restart during a retry wait and a full queue on real Redis"
```

---

### Task 8: Architecture documents

**Files:**
- Create: `docs/architect/adr/0012-notification-status-tracking.md`
- Create: `docs/architect/adr/0013-delivery-through-slimmessagebus-redis-queue.md`
- Modify: `docs/architect/adr/0002-no-relational-database.md`, `0003-accept-then-deliver-in-process.md`, `0011-slimmessagebus-in-process-mediator.md`
- Modify: `docs/architect/01-scope.md`, `02-domain.md`, `03-integration.md`, `04-data.md`, `05-quality.md`, `README.md`
- Modify: `docs/operator-guide.md`, `AGENTS.md`
- Modify: `docs/architect/diagrams/runtime.architecture.json`, `notification-lifecycle.lifecycle.json`, `send-email.sequence.json`, `send-teams.sequence.json`, `send-email-graph.sequence.json`

- [ ] **Step 1: Write ADR-0012 and ADR-0013** in the house format (`- **Status:**`, `- **Context:**`, `- **Decision:**`, `- **Alternatives:**`, `- **Consequences:**`, bullet style, plain English — read ADR-0002 and ADR-0003 first and match them).
  - ADR-0012 decision: spec D1–D4, §7, the 24 h retention, best-effort writes, `pending` before publish. Alternatives: 404 until ended (rejected: a polling caller cannot tell "not yet" from "wrong id"); `skipped` as its own value (rejected: shows channel set-up, the requester's rule); 403 for another caller (rejected: confirms the id exists).
  - ADR-0013 decision: spec §3–§6, D5–D8, D10. Alternatives: keep the in-process queue (rejected: loses every waiting notification on each deploy); SMB in-process `Retry()` (rejected: waits in memory); Azure Service Bus (deferred: new infrastructure; the upgrade path for at-least-once); Redis Streams built by hand (rejected: our own queue to maintain). Consequences: at-most-once per pop, loss bound of 1 message per replica on a hard crash, Redis data loss loses the queue, personal data in Redis until delivered, the schema-compatibility rule, `QueueCapacity` now service-wide.
- [ ] **Step 2: Amend ADR-0002** (Status: "Accepted; amended by ADR-0012 and ADR-0013" and a short amendment bullet), mark **ADR-0003** "Superseded by ADR-0013", and add one line to **ADR-0011** that a second child bus now carries delivery.
- [ ] **Step 3: Update 01–05 and README:**
  - `01-scope`: status tracking in scope; listing/history out.
  - `02-domain`: `Notification.Resume`; the internal → public status table (spec §7).
  - `03-integration`: POST answers 200 (and the 4-hour 202 replay note), the reworded Queued/Skipped rule (spec §8), the GET route with its table, evaluation order step 9, the `503` wording.
  - `04-data`: entities `DeliverNotification` (Redis list) and `NotificationStatusRecord` (Redis key) with the house table columns; Storage and Retention rows; the personal-data note.
  - `05-quality`: the loss/duplicate guarantees (spec §10), the two new log events (2007, 2008), the gauge's new meaning.
  - `README.md`: ADR index.
- [ ] **Step 4: Update `docs/operator-guide.md`** (Redis now holds delivery; `QueueCapacity` is service-wide — raise it with replicas; `Notifications:Status:RetentionHours`; the gauge change; dashboards built on the per-replica value) and **`AGENTS.md`** (architecture bullets: delivery consumer instead of the worker; `DeliveryWorkerTests.TestClock` reference becomes `DeliveryConsumerTests` with `FakeTimeProvider`; the `GET` route; the status store).
- [ ] **Step 5: Update the diagram JSON sources** for the changed flows (queue node becomes the Redis list; the worker becomes the consumer; the GET path; the lifecycle's public statuses). Do not hand-edit the SVGs.
- [ ] **Step 6: Commit**

```bash
git add docs AGENTS.md
git commit -m "docs: ADR-0012 status tracking, ADR-0013 Redis-queue delivery, and the architecture updates"
```

---

## Self-review notes

- Spec §2 D1–D10 → Tasks 2 (D1, D3, D4), 6 (D2, D3, D9), 3 (D5, D10), 5 (D6, D8 by placement), 4 (D7).
- Spec §5 compatibility rule → ADR-0013 (Task 8); `SchemaVersion` field (Task 3).
- Spec §6 every branch → Task 3 tests; acceptance order → Task 4 test with `OnPublish`.
- Spec §9 settings → Task 5 (binding, startup test), `appsettings.json`.
- Spec §11 BDD list → Task 7.
- Spec §12 → Task 8.
