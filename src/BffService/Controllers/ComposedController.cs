using BffService.Composers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BffService.Controllers;

[ApiController]
[Route("api/composed")]
[Authorize]
public class ComposedController : ControllerBase
{
    private readonly IOrderDetailComposer _orderDetailComposer;

    public ComposedController(IOrderDetailComposer orderDetailComposer)
    {
        _orderDetailComposer = orderDetailComposer;
    }

    /// <summary>
    /// Returns a full order view composed from OrderService, UserAuthService, and CatalogService.
    /// If a downstream service is unavailable, that section returns null instead of failing the whole request.
    /// </summary>
    [HttpGet("orders/{id}")]
    public async Task<IActionResult> GetOrderDetail(int id, CancellationToken ct)
    {
        var view = await _orderDetailComposer.ComposeAsync(id, ct);
        return view is null
            ? NotFound(new { message = $"Order {id} not found." })
            : Ok(view);
    }
}
