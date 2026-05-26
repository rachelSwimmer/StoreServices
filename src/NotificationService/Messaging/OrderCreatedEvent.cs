namespace NotificationService.Messaging;

/// <summary>
/// NotificationService's OWN copy of the "order created" event contract.
///
/// This is a DIFFERENT file/type from OrderService's OrderCreatedEvent on
/// purpose. The services never share a compiled type — they agree only on the
/// JSON field names produced on the wire. This consumer is a "tolerant reader":
/// it declares only the fields it actually needs to send a notification. If the
/// producer adds extra fields, JSON deserialization simply ignores them here, so
/// the producer can evolve and redeploy without breaking this service.
/// </summary>
public record OrderCreatedEvent
{
    public int OrderId { get; init; }
    public int UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTime OrderDate { get; init; }
}
