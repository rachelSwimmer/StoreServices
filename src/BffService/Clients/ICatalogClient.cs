using BffService.DTOs;

namespace BffService.Clients;

public interface ICatalogClient
{
    Task<ProductStockDto> GetProductStockAsync(int productId);
}
