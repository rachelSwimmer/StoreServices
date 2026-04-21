using Microsoft.AspNetCore.Mvc;
using ProductCatalogService.Interfaces;

namespace ProductCatalogService.Controllers;

[ApiController]
[Route("internal/products")]
public class InternalProductsController : ControllerBase
{
    private readonly IProductRepository _productRepository;

    public InternalProductsController(IProductRepository productRepository)
    {
        _productRepository = productRepository;
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
}
