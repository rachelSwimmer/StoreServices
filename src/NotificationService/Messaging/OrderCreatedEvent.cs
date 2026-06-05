namespace NotificationService.Messaging;

/// <summary>
/// NotificationService's own copy of the wire contract — declares only the
/// fields it needs (tolerant reader). Duplicated on purpose so producer and
/// consumer can evolve independently.
/// </summary>
public record OrderCreatedEvent
{
    public int OrderId { get; init; }
    public int UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTime OrderDate { get; init; }
}
