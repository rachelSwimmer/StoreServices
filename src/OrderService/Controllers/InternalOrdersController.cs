using Microsoft.AspNetCore.Mvc;
using OrderService.Interfaces;

namespace OrderService.Controllers;

[ApiController]
[Route("internal/orders")]
public class InternalOrdersController : ControllerBase
{
    private readonly IOrderRepository _orderRepository;

    public InternalOrdersController(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var order = await _orderRepository.GetByIdAsync(id);
        if (order == null)
            return NotFound();

        return Ok(new
        {
            id           = order.Id,
            userId       = order.UserId,
            status       = order.Status,
            shippingAddress = order.ShippingAddress,
            totalAmount  = order.TotalAmount,
            orderDate    = order.OrderDate,
            shippedDate  = order.ShippedDate,
            deliveredDate = order.DeliveredDate,
            items = order.OrderItems.Select(i => new
            {
                productId   = i.ProductId,
                productName = i.ProductName,
                quantity    = i.Quantity,
                unitPrice   = i.UnitPrice,
                subtotal    = i.Subtotal
            })
        });
    }
}
