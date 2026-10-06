using System.Text;
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
    public async Task A_status_is_stored_by_name_and_a_record_stored_with_a_number_still_reads()
    {
        var cache = Cache();
        var store = Store(cache);
        var id = Guid.CreateVersion7();

        await store.WriteAsync("c", new NotificationStatusRecord(id, null, NotificationOutcome.Success), CancellationToken.None);

        Encoding.UTF8.GetString((await cache.GetAsync(NotificationStatusStore.KeyOf("c", id)))!).ShouldContain("\"Status\":\"Success\"");
        var older = Guid.CreateVersion7();
        await cache.SetAsync(
            NotificationStatusStore.KeyOf("c", older),
            Encoding.UTF8.GetBytes($$"""{"NotificationId":"{{older}}","IdempotencyKey":null,"Status":{{(int)NotificationOutcome.Failed}}}"""));
        (await store.ReadAsync("c", older, CancellationToken.None)).ShouldBe(new NotificationStatusRecord(older, null, NotificationOutcome.Failed));
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
