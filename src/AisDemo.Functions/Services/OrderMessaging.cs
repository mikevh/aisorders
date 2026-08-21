using System.Collections.Concurrent;
using AisDemo.Functions.Models;
using Azure.Messaging.ServiceBus;

namespace AisDemo.Functions.Services;

/// <summary>
/// Sends to the orders queue and publishes to the order-events topic.
/// </summary>
public sealed class OrderMessaging : IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly DemoOptions _options;
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

    public OrderMessaging(ServiceBusClient client, DemoOptions options)
    {
        _client = client;
        _options = options;
    }

    private ServiceBusSender SenderFor(string entity) =>
        _senders.GetOrAdd(entity, name => _client.CreateSender(name));

    /// <summary>Enqueues a submitted order for asynchronous processing.</summary>
    public Task SendOrderAsync(OrderMessage message, CancellationToken ct = default)
    {
        var busMessage = new ServiceBusMessage(JsonDefaults.Serialize(message))
        {
            ContentType = "application/json",
            MessageId = message.OrderId,
            CorrelationId = message.CorrelationId
        };

        busMessage.ApplicationProperties["orderId"] = message.OrderId;
        busMessage.ApplicationProperties["correlationId"] = message.CorrelationId;

        return SenderFor(_options.QueueName).SendMessageAsync(busMessage, ct);
    }

    /// <summary>Publishes a terminal-outcome event to the topic.</summary>
    /// <remarks>
    /// The three application properties below are what subscription filters
    /// actually evaluate. Filters read message properties, never the body — set
    /// these only in the JSON and the notifications subscription receives
    /// nothing, silently, with no error anywhere. See SPEC.md 6.2 and 6.3.
    /// </remarks>
    public Task PublishEventAsync(OrderEvent evt, CancellationToken ct = default)
    {
        var busMessage = new ServiceBusMessage(JsonDefaults.Serialize(evt))
        {
            ContentType = "application/json",
            MessageId = evt.EventId,
            CorrelationId = evt.CorrelationId,
            Subject = evt.EventType
        };

        busMessage.ApplicationProperties["eventType"] = evt.EventType;
        busMessage.ApplicationProperties["orderTotal"] = (double)evt.OrderTotal;
        busMessage.ApplicationProperties["customerId"] = evt.CustomerId;
        busMessage.ApplicationProperties["orderId"] = evt.OrderId;
        busMessage.ApplicationProperties["correlationId"] = evt.CorrelationId;

        return SenderFor(_options.TopicName).SendMessageAsync(busMessage, ct);
    }

    /// <summary>
    /// Receiver over the orders dead-letter queue, used by ReplayDeadLetter.
    /// </summary>
    public ServiceBusReceiver CreateDeadLetterReceiver() =>
        _client.CreateReceiver(_options.QueueName, new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter,
            ReceiveMode = ServiceBusReceiveMode.PeekLock
        });

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync();
        }
    }
}
