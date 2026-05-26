namespace SharedKernel.Messaging;

/// <summary>
/// Publishes a domain event to the message broker.
///
/// Note the signature is generic (<typeparamref name="T"/>) and takes a routing
/// key — this helper knows nothing about orders or any other business type. It
/// only knows "serialize this object to JSON and publish it under this routing
/// key". That keeps it as reusable *technical plumbing* in SharedKernel, while
/// the actual message contracts live inside each service.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Serialize <paramref name="message"/> to JSON and publish it to the
    /// shared topic exchange using the given <paramref name="routingKey"/>.
    /// Implementations swallow/log broker errors so a publish failure never
    /// breaks the calling business operation.
    /// </summary>
    void Publish<T>(string routingKey, T message);
}
