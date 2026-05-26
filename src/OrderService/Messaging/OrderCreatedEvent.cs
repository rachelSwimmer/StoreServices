namespace OrderService.Messaging;

/// <summary>
/// OrderService's OWN copy of the "order created" event contract.
///
/// IMPORTANT TEACHING POINT: NotificationService has its own, separate copy of
/// this same shape. The two services are NOT sharing a compiled .NET type — they
/// only agree on the JSON field names on the wire ("tolerant reader" pattern).
/// That is what lets each service be deployed independently: OrderService could
/// add a field here and ship without forcing NotificationService to rebuild.
/// </summary>
public record OrderCreatedEvent
{
    public int OrderId { get; init; }
    public int UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public DateTime OrderDate { get; init; }
    public List<OrderCreatedItem> Items { get; init; } = new();
}

public record OrderCreatedItem
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
}
