using System.Text;
using System.Text.Json;
using NotificationService.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SharedKernel.Messaging;

namespace NotificationService.Consumers;

/// <summary>
/// Background worker that consumes "order created" events from RabbitMQ and
/// "sends" a confirmation notification (here: logs it).
///
/// This is the teaching centerpiece — it shows, with the raw RabbitMQ.Client
/// API, every step a consumer performs:
///   connection -> channel -> declare exchange -> declare queue -> bind queue
///   -> set prefetch -> consume -> handle message -> acknowledge.
///
/// It's a BackgroundService (like ProductCatalogService's ReservationSweeper):
/// it starts with the app, runs until shutdown, and keeps its RabbitMQ
/// connection open for the lifetime of the process.
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
        // The broker may not be reachable the instant we start (Docker startup
        // ordering). Retry the initial connection a few times before giving up,
        // mirroring the DB-migrate retry loop in OrderService's Program.cs.
        await ConnectWithRetryAsync(stoppingToken);

        if (_channel is null)
        {
            _logger.LogError("Could not establish a RabbitMQ channel; consumer will not run.");
            return;
        }

        // --- AMQP topology setup ----------------------------------------------
        // Declare the same exchange the producer uses. Idempotent: whoever starts
        // first creates it. type = "topic", durable so it survives a restart.
        _channel.ExchangeDeclare(
            exchange: MessagingTopology.ExchangeName,
            type: MessagingTopology.ExchangeType,
            durable: true,
            autoDelete: false);

        // Declare OUR queue. Unlike the exchange, the queue belongs to this
        // consumer's concern ("notifications"). durable = survive broker restart;
        // not exclusive/autoDelete so messages accumulate even while we're down.
        _channel.QueueDeclare(
            queue: MessagingTopology.NotificationsQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        // Bind the queue to the exchange with a routing-key pattern. THIS is what
        // makes messages flow: the topic exchange copies any message whose
        // routing key matches "order.*" into our queue.
        _channel.QueueBind(
            queue: MessagingTopology.NotificationsQueue,
            exchange: MessagingTopology.ExchangeName,
            routingKey: MessagingTopology.NotificationsBindingPattern);

        // Fair dispatch: don't hand this consumer a new message until it has
        // acked the previous one. With prefetch=1 a slow consumer won't get
        // flooded, and work spreads evenly if you scale to multiple instances.
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        _logger.LogInformation(
            "Listening for '{RoutingKey}' on queue '{Queue}'",
            MessagingTopology.NotificationsBindingPattern, MessagingTopology.NotificationsQueue);

        // --- Wire up the consumer callback ------------------------------------
        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += OnMessageReceived;

        // autoAck: false -> we acknowledge manually AFTER we've handled the
        // message successfully. If we crash mid-handling without acking, RabbitMQ
        // redelivers the message (at-least-once delivery).
        _channel.BasicConsume(
            queue: MessagingTopology.NotificationsQueue,
            autoAck: false,
            consumer: consumer);

        // BasicConsume returns immediately; the Received event fires on a broker
        // thread. Keep this background task alive until the app shuts down.
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private void OnMessageReceived(object? sender, BasicDeliverEventArgs ea)
    {
        try
        {
            // Read the raw bytes and deserialize into OUR OWN OrderCreatedEvent
            // copy. The producer's type is never referenced here.
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            var order = JsonSerializer.Deserialize<OrderCreatedEvent>(json);

            if (order is not null)
            {
                // The "side effect" — in a real system, send an email/SMS/push.
                _logger.LogInformation(
                    "📧 Sending order confirmation to {UserName} for order #{OrderId} (total ${TotalAmount})",
                    order.UserName, order.OrderId, order.TotalAmount);
            }
            else
            {
                _logger.LogWarning("Received an empty/unparseable OrderCreated message");
            }

            // Acknowledge: tell RabbitMQ we handled it so it can drop the message.
            _channel!.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling OrderCreated message");
            // Nack and don't requeue (requeue: false) to avoid a poison-message
            // loop. In production this would route to a dead-letter queue.
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
