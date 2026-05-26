using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace SharedKernel.Messaging;

/// <summary>
/// A thin RabbitMQ publisher built directly on the raw RabbitMQ.Client library
/// (no MassTransit/abstraction) so the AMQP steps stay visible for teaching.
///
/// Lifetime: register this as a SINGLETON. A RabbitMQ connection is expensive to
/// open, is thread-safe, and is designed to be long-lived and shared. We open
/// one connection + one channel ("IModel") up front and reuse them.
/// </summary>
public sealed class RabbitMqPublisher : IEventPublisher, IDisposable
{
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly IConnection? _connection;
    private readonly IModel? _channel;

    public RabbitMqPublisher(RabbitMqSettings settings, ILogger<RabbitMqPublisher> logger)
    {
        _logger = logger;

        try
        {
            // 1. A ConnectionFactory holds the broker address + credentials and
            //    knows how to open TCP connections to RabbitMQ.
            var factory = new ConnectionFactory
            {
                HostName = settings.HostName,
                Port = settings.Port,
                UserName = settings.UserName,
                Password = settings.Password,
                // Let the client transparently recover the connection/channel
                // if the broker restarts or the network blips.
                AutomaticRecoveryEnabled = true
            };

            // 2. Open the connection, then a channel. Almost all AMQP operations
            //    (declare, publish, consume) happen on a channel, not the
            //    connection directly.
            _connection = factory.CreateConnection("order-service-publisher");
            _channel = _connection.CreateModel();

            // 3. Declare the exchange. This is idempotent — if it already exists
            //    with the same settings it's a no-op, otherwise it's created.
            //    Declaring on both publish and consume sides means whoever starts
            //    first creates it; neither depends on the other's ordering.
            _channel.ExchangeDeclare(
                exchange: MessagingTopology.ExchangeName,
                type: MessagingTopology.ExchangeType,   // "topic"
                durable: true,      // survive a broker restart
                autoDelete: false);

            _logger.LogInformation(
                "RabbitMQ publisher connected to {Host}:{Port}, exchange '{Exchange}' ready",
                settings.HostName, settings.Port, MessagingTopology.ExchangeName);
        }
        catch (Exception ex)
        {
            // Teaching choice: we do NOT rethrow. If the broker is down at
            // startup the service still runs and serves HTTP; publishes simply
            // no-op (and log a warning) until RabbitMQ is reachable. In a real
            // system you'd pair this with the transactional outbox pattern so no
            // event is silently lost — see docs/rabbitmq-example.md.
            _logger.LogError(ex,
                "Could not connect to RabbitMQ at {Host}:{Port}. Events will not be published.",
                settings.HostName, settings.Port);
        }
    }

    public void Publish<T>(string routingKey, T message)
    {
        if (_channel is null)
        {
            _logger.LogWarning(
                "Skipping publish of '{RoutingKey}' — no RabbitMQ channel available.", routingKey);
            return;
        }

        try
        {
            // 4. Serialize the message to a JSON byte array. JSON is the wire
            //    contract — the consumer reads these same bytes and deserializes
            //    into ITS OWN copy of the event type.
            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            // 5. Mark the message persistent (DeliveryMode = 2) so it survives a
            //    broker restart while sitting in a durable queue. Tag the
            //    content type so consumers/tools know it's JSON.
            var props = _channel.CreateBasicProperties();
            props.ContentType = "application/json";
            props.DeliveryMode = 2; // persistent

            // 6. Publish to the exchange with a routing key. We publish to the
            //    EXCHANGE, never to a queue directly — the exchange decides which
            //    queues get the message based on bindings.
            _channel.BasicPublish(
                exchange: MessagingTopology.ExchangeName,
                routingKey: routingKey,
                basicProperties: props,
                body: body);

            _logger.LogInformation(
                "Published message to exchange '{Exchange}' with routing key '{RoutingKey}'",
                MessagingTopology.ExchangeName, routingKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message with routing key '{RoutingKey}'", routingKey);
        }
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
