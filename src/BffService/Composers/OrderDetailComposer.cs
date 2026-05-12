using BffService.Clients;
using BffService.DTOs;

namespace BffService.Composers;

public class OrderDetailComposer : IOrderDetailComposer
{
    private readonly IOrderClient _orderClient;
    private readonly IUserClient _userClient;
    private readonly ICatalogClient _catalogClient;
    private readonly ILogger<OrderDetailComposer> _logger;

    public OrderDetailComposer(
        IOrderClient orderClient,
        IUserClient userClient,
        ICatalogClient catalogClient,
        ILogger<OrderDetailComposer> logger)
    {
        _orderClient = orderClient;
        _userClient = userClient;
        _catalogClient = catalogClient;
        _logger = logger;
    }

    public async Task<OrderDetailView?> ComposeAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderClient.GetOrderAsync(orderId);
        if (order == null) return null;

        var userTask = SafeFetch(
            () => _userClient.GetUserSummaryAsync(order.UserId),
            $"user {order.UserId}");

        var productTasks = order.Items
            .Select(item => SafeFetch(
                () => _catalogClient.GetProductStockAsync(item.ProductId),
                $"product {item.ProductId}"))
            .ToList();

        await Task.WhenAll([userTask, .. productTasks]);

        var user = await userTask;
        var stocks = await Task.WhenAll(productTasks);

        return new OrderDetailView
        {
            OrderId         = order.Id,
            Status          = order.Status,
            ShippingAddress = order.ShippingAddress,
            TotalAmount     = order.TotalAmount,
            OrderDate       = order.OrderDate,
            ShippedDate     = order.ShippedDate,
            DeliveredDate   = order.DeliveredDate,
            User = user == null ? null : new UserView
            {
                Id        = user.Id,
                FirstName = user.FirstName,
                LastName  = user.LastName,
                Email     = user.Email
            },
            Items = order.Items.Select((item, index) => new OrderItemView
            {
                ProductId    = item.ProductId,
                ProductName  = item.ProductName,
                Quantity     = item.Quantity,
                UnitPrice    = item.UnitPrice,
                Subtotal     = item.Subtotal,
                CurrentStock = stocks[index]?.Stock
            }).ToList()
        };
    }

    private async Task<T?> SafeFetch<T>(Func<Task<T>> fetch, string context) where T : class
    {
        try
        {
            return await fetch();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch {Context} — returning partial data", context);
            return null;
        }
    }
}
