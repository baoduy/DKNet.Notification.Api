using System.Collections.Concurrent;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace DKNet.Notification.App.BDDTests.Support;

/// <summary>
/// One real Redis container per feature (DRK-2013 §3b: the idempotency scenarios run against a real Redis; one per
/// feature because features run in parallel, see <see cref="FeatureKey" />), started on first use and disposed by
/// <see cref="ApiHooks" /> after the run. Needs Docker.
/// </summary>
public static class RedisServer
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<RedisContainer>>> Containers = new();

    /// <summary>The connection string of the calling feature's container.</summary>
    public static async Task<string> ConnectionStringAsync() => (await ContainerAsync()).GetConnectionString();

    /// <summary>Removes every key, so a scenario starts from an empty store.</summary>
    public static async Task FlushAsync()
    {
        var result = await (await ContainerAsync()).ExecAsync(["redis-cli", "FLUSHALL"]);
        result.ExitCode.ShouldBe(0, result.Stderr);
    }

    /// <summary>Every key in the store with its value as text (strings and hashes), for content checks.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> DumpAsync()
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(
            $"{await ConnectionStringAsync()},allowAdmin=true");
        var database = connection.GetDatabase();
        var dump = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in connection.GetServers().SelectMany(server => server.Keys()))
        {
            dump[key.ToString()] = await database.KeyTypeAsync(key) switch
            {
                RedisType.String => (await database.StringGetAsync(key)).ToString(),
                RedisType.Hash => string.Join(
                    '\n',
                    (await database.HashGetAllAsync(key)).Select(entry => $"{entry.Name}={entry.Value}")),
                var type => $"<{type}>"
            };
        }

        return dump;
    }

    internal static async Task StopAsync()
    {
        foreach (var container in Containers.Values.Where(c => c.IsValueCreated))
        {
            await (await container.Value).DisposeAsync();
        }
    }

    private static Task<RedisContainer> ContainerAsync() =>
        Containers.GetOrAdd(FeatureKey.Current, _ => new Lazy<Task<RedisContainer>>(StartAsync)).Value;

    private static async Task<RedisContainer> StartAsync()
    {
        var container = new RedisBuilder("redis:7.4-alpine").Build();
        await container.StartAsync();
        return container;
    }
}
