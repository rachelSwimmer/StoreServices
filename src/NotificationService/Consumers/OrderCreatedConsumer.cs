using System.Text;
using System.Text.Json;
using NotificationService.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SharedKernel.Messaging;

namespace NotificationService.Consumers;

/// <summary>
/// BackgroundService that consumes "order created" events from RabbitMQ and
/// "sends" a confirmation (here: logs it). Built directly on RabbitMQ.Client so
/// the AMQP steps stay visible. See docs/rabbitmq-example.md.
/// </summary>
public class OrderCreatedConsumer : BackgroundService
{
    private readonly RabbitMqSettings _settings;
    private readonly ILogger<OrderCreatedConsumer> _logger;

    private IConnection? _connection;
    private IModel? _channel;

    public OrderCreatedConsumer(RabbitMqSettings settings, ILogger<OrderCreatedConsumer> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ConnectWithRetryAsync(stoppingToken);

        if (_channel is null)
        {
            _logger.LogError("Could not establish a RabbitMQ channel; consumer will not run.");
            return;
        }

        // Declared on both publish and consume sides — whoever starts first creates it.
        _channel.ExchangeDeclare(
            exchange: MessagingTopology.ExchangeName,
            type: MessagingTopology.ExchangeType,
            durable: true,
            autoDelete: false);

        _channel.QueueDeclare(
            queue: MessagingTopology.NotificationsQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        _channel.QueueBind(
            queue: MessagingTopology.NotificationsQueue,
            exchange: MessagingTopology.ExchangeName,
            routingKey: MessagingTopology.NotificationsBindingPattern);

        // Fair dispatch: at most one unacked message per consumer at a time, so work spreads evenly if scaled.
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        _logger.LogInformation(
            "Listening for '{RoutingKey}' on queue '{Queue}'",
            MessagingTopology.NotificationsBindingPattern, MessagingTopology.NotificationsQueue);

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += OnMessageReceived;

        // autoAck:false → ack manually AFTER handling. If we crash mid-handling, RabbitMQ redelivers (at-least-once).
        _channel.BasicConsume(
            queue: MessagingTopology.NotificationsQueue,
            autoAck: false,
            consumer: consumer);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private void OnMessageReceived(object? sender, BasicDeliverEventArgs ea)
    {
        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            var order = JsonSerializer.Deserialize<OrderCreatedEvent>(json);

            if (order is not null)
            {
                _logger.LogInformation(
                    "📧 Sending order confirmation to {UserName} for order #{OrderId} (total ${TotalAmount})",
                    order.UserName, order.OrderId, order.TotalAmount);
            }
            else
            {
                _logger.LogWarning("Received an empty/unparseable OrderCreated message");
            }

            _channel!.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling OrderCreated message");
            // requeue:false to avoid a poison-message loop; in production this would route to a DLQ.
            _channel!.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    private async Task ConnectWithRetryAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _settings.HostName,
            Port = _settings.Port,
            UserName = _settings.UserName,
            Password = _settings.Password,
            AutomaticRecoveryEnabled = true
        };

        var retries = 10;
        while (retries > 0 && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                _connection = factory.CreateConnection("notification-service-consumer");
                _channel = _connection.CreateModel();
                _logger.LogInformation(
                    "Connected to RabbitMQ at {Host}:{Port}", _settings.HostName, _settings.Port);
                return;
            }
            catch (Exception ex)
            {
                retries--;
                _logger.LogWarning(
                    "RabbitMQ not ready, retrying in 5s ({Retries} attempts left): {Error}",
                    retries, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
