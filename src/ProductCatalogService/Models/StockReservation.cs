namespace ProductCatalogService.Models;

public class StockReservation
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public string Status { get; set; } = "Reserved"; // Reserved, Released
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Product Product { get; set; } = null!;
}
