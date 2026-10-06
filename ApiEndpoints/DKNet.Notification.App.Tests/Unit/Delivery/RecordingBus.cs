using System.Collections.Concurrent;
using DKNet.Notification.AppServices.Delivery;
using SlimMessageBus;

namespace DKNet.Notification.App.Tests.Unit.Delivery;

/// <summary>A bus that records every <see cref="DeliverNotification" /> published to it; only <c>Publish</c> works.</summary>
internal sealed class RecordingBus : IMessageBus
{
    private readonly ConcurrentQueue<DeliverNotification> _published = new();

    public IReadOnlyCollection<DeliverNotification> Published => _published.ToArray();

    /// <summary>Makes every publish throw, the way a lost Redis connection would.</summary>
    public bool ThrowOnPublish { get; set; }

    /// <summary>Runs with each message just before it is recorded, so a test can see what exists at publish time.</summary>
    public Action<DeliverNotification>? OnPublish { get; set; }

    public Task Publish<TMessage>(TMessage message, string? path = null, IDictionary<string, object>? headers = null, CancellationToken cancellationToken = default)
    {
        // The text the real bus puts in its error: it prints the message.
        if (ThrowOnPublish) throw new InvalidOperationException($"Producing message {message} of type {typeof(TMessage).Name} failed: redis://secret@host");
        var delivery = (DeliverNotification)(object)message!;
        OnPublish?.Invoke(delivery);
        _published.Enqueue(delivery);
        return Task.CompletedTask;
    }

    // The rest of IMessageBus is not used by the code under test.
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, string? path = null, IDictionary<string, object>? headers = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task Send(IRequest request, string? path = null, IDictionary<string, object>? headers = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TResponse> Send<TResponse, TRequest>(TRequest request, string? path = null, IDictionary<string, object>? headers = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
