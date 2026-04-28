namespace OrderService.Clients;

public interface ICatalogClient
{
    Task<ProductInfoDto> GetProductInfoAsync(int productId);
    Task<int> ReserveStockAsync(int productId, int quantity);
    Task ReleaseReservationAsync(int reservationId);
}

public class ProductInfoDto
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
}
