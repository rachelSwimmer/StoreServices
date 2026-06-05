namespace OrderService.Messaging;

/// <summary>
/// OrderService's own copy of the wire contract. NotificationService has its
/// own separate copy — services agree only on the JSON field names, not a
/// shared .NET type, so each can evolve and ship independently.
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
