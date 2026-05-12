using Microsoft.AspNetCore.Mvc;
using ProductCatalogService.Interfaces;

namespace ProductCatalogService.Controllers;

[ApiController]
[Route("internal/products")]
public class InternalProductsController : ControllerBase
{
    private static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(5);

    private readonly IProductRepository _productRepository;
    private readonly IStockReservationRepository _reservationRepository;

    public InternalProductsController(
        IProductRepository productRepository,
        IStockReservationRepository reservationRepository)
    {
        _productRepository = productRepository;
        _reservationRepository = reservationRepository;
    }

    [HttpGet("{id}/price-and-stock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPriceAndStock(int id)
    {
        var product = await _productRepository.GetByIdAsync(id);

        if (product == null)
            return NotFound();

        return Ok(new
        {
            productId = product.Id,
            name = product.Name,
            price = product.Price,
            stock = product.Stock
        });
    }

    public record ReserveStockRequest(int ProductId, int Quantity);
    public record ReleaseReservationRequest(int ReservationId);
    public record ConfirmReservationRequest(int ReservationId);

    [HttpPost("reserve-stock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReserveStock([FromBody] ReserveStockRequest request)
    {
        try
        {
            var reservationId = await _reservationRepository.ReserveAsync(
                request.ProductId, request.Quantity, ReservationTtl);
            return Ok(new { reservationId });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("release-reservation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReleaseReservation([FromBody] ReleaseReservationRequest request)
    {
        var released = await _reservationRepository.ReleaseAsync(request.ReservationId);
        return released ? Ok() : NotFound();
    }

    [HttpPost("confirm-reservation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmReservation([FromBody] ConfirmReservationRequest request)
    {
        var confirmed = await _reservationRepository.ConfirmAsync(request.ReservationId);
        return confirmed ? Ok() : NotFound();
    }
}
