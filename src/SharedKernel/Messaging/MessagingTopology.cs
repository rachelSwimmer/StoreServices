namespace SharedKernel.Messaging;

/// <summary>
/// The RabbitMQ "topology" — the names of the exchange, queues and routing keys
/// that producers and consumers must agree on.
///
/// This IS deliberately shared between services, because it is the *contract*
/// both sides bind to. (Contrast with the message body type itself — see the
/// per-service OrderCreatedEvent classes — which we duplicate on purpose so the
/// services stay independently deployable.)
///
/// AMQP refresher for students:
///   producer --(routing key)--> [exchange] --(binding)--> [queue] --> consumer
///
/// We use a *topic* exchange. A topic exchange routes a message to every queue
/// whose binding pattern matches the message's routing key. Patterns may use
/// "*" (exactly one word) and "#" (zero or more words), where words are
/// separated by dots. So a queue bound with "order.*" receives "order.created",
/// "order.cancelled", etc. — which lets us add more order events later without
/// touching existing consumers.
/// </summary>
public static class MessagingTopology
{
    /// <summary>The single topic exchange all "store" domain events flow through.</summary>
    public const string ExchangeName = "store.events";

    /// <summary>Exchange type — see RabbitMQ.Client's ExchangeType.Topic.</summary>
    public const string ExchangeType = "topic";

    /// <summary>Routing key the producer stamps on an "order created" message.</summary>
    public const string OrderCreatedRoutingKey = "order.created";

    /// <summary>The queue NotificationService consumes from.</summary>
    public const string NotificationsQueue = "notifications.order-created";

    /// <summary>
    /// Binding pattern for the notifications queue. "order.*" matches any single
    /// "order.something" routing key, so this consumer picks up order.created
    /// today and any future order.* event tomorrow.
    /// </summary>
    public const string NotificationsBindingPattern = "order.*";
}
