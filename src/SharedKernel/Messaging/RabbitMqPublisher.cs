using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace SharedKernel.Messaging;

/// <summary>
/// Thin RabbitMQ publisher built directly on RabbitMQ.Client so the AMQP steps
/// stay visible. Register as a SINGLETON — the connection is expensive to open,
/// is thread-safe, and is designed to be long-lived.
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
            var factory = new ConnectionFactory
            {
                HostName = settings.HostName,
                Port = settings.Port,
                UserName = settings.UserName,
                Password = settings.Password,
                AutomaticRecoveryEnabled = true
            };

            _connection = factory.CreateConnection("order-service-publisher");
            _channel = _connection.CreateModel();

            // Declared on both publish and consume sides so neither depends on the other's startup order.
            _channel.ExchangeDeclare(
                exchange: MessagingTopology.ExchangeName,
                type: MessagingTopology.ExchangeType,
                durable: true,
                autoDelete: false);

            _logger.LogInformation(
                "RabbitMQ publisher connected to {Host}:{Port}, exchange '{Exchange}' ready",
                settings.HostName, settings.Port, MessagingTopology.ExchangeName);
        }
        catch (Exception ex)
        {
            // Don't rethrow: the service still serves HTTP if the broker is down at startup; publishes
            // no-op until RabbitMQ is reachable. A real system would pair this with a transactional outbox.
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
            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            var props = _channel.CreateBasicProperties();
            props.ContentType = "application/json";
            props.DeliveryMode = 2; // persistent — survives broker restart in a durable queue

            // Publish to the EXCHANGE, never to a queue directly — bindings decide which queues receive it.
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
