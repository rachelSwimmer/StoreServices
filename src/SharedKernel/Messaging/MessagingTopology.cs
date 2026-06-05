namespace SharedKernel.Messaging;

/// <summary>
/// Exchange/queue/routing-key names that producers and consumers must agree on.
/// This IS shared — it's the contract both sides bind to. The message body
/// types (<c>OrderCreatedEvent</c>) are deliberately duplicated per service so
/// services stay independently deployable. See docs/rabbitmq-example.md.
/// </summary>
public static class MessagingTopology
{
    public const string ExchangeName = "store.events";
    public const string ExchangeType = "topic";

    public const string OrderCreatedRoutingKey = "order.created";

    public const string NotificationsQueue = "notifications.order-created";

    // "order.*" matches any order.something key, so this consumer also picks up future order.* events.
    public const string NotificationsBindingPattern = "order.*";
}
