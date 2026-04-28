using BffService.Clients;
using BffService.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BffService.Controllers;

[ApiController]
[Route("api/composed")]
[Authorize]
public class ComposedController : ControllerBase
{
    private readonly IOrderClient _orderClient;
    private readonly IUserClient _userClient;
    private readonly ICatalogClient _catalogClient;
    private readonly ILogger<ComposedController> _logger;

    public ComposedController(
        IOrderClient orderClient,
        IUserClient userClient,
        ICatalogClient catalogClient,
        ILogger<ComposedController> logger)
    {
        _orderClient = orderClient;
        _userClient = userClient;
        _catalogClient = catalogClient;
        _logger = logger;
    }

    /// <summary>
    /// Returns a full order view composed from OrderService, UserAuthService, and CatalogService.
    /// If a downstream service is unavailable, that section returns null instead of failing the whole request.
    /// </summary>
    [HttpGet("orders/{id}")]
    public async Task<IActionResult> GetOrderDetail(int id)
    {
        // Step 1 — fetch the order first; everything else depends on it
        var order = await _orderClient.GetOrderAsync(id);
        if (order == null)
            return NotFound(new { message = $"Order {id} not found." });

        // Step 2 — fan out: fetch user + all product stocks IN PARALLEL
        // SafeFetch catches exceptions and returns null so one failed service
        // does not bring down the whole composed response (graceful degradation)
        var userTask = SafeFetch(
            () => _userClient.GetUserSummaryAsync(order.UserId),
            $"user {order.UserId}");

        var productTasks = order.Items
            .Select(item => SafeFetch(
                () => _catalogClient.GetProductStockAsync(item.ProductId),
                $"product {item.ProductId}"))
            .ToList();

        await Task.WhenAll([userTask, .. productTasks]);

        // Step 3 — compose
        var user = await userTask;
        var stocks = await Task.WhenAll(productTasks);

        var view = new OrderDetailView
        {
            OrderId      = order.Id,
            Status       = order.Status,
            ShippingAddress = order.ShippingAddress,
            TotalAmount  = order.TotalAmount,
            OrderDate    = order.OrderDate,
            ShippedDate  = order.ShippedDate,
            DeliveredDate = order.DeliveredDate,
            User = user == null ? null : new UserView
            {
                Id        = user.Id,
                FirstName = user.FirstName,
                LastName  = user.LastName,
                Email     = user.Email
            },
            Items = order.Items.Select((item, index) => new OrderItemView
            {
                ProductId   = item.ProductId,
                ProductName = item.ProductName,
                Quantity    = item.Quantity,
                UnitPrice   = item.UnitPrice,
                Subtotal    = item.Subtotal,
                CurrentStock = stocks[index]?.Stock  // null if CatalogService was down
            }).ToList()
        };

        return Ok(view);
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
