using System.ComponentModel.DataAnnotations;

namespace ProductCatalogService.DTOs;

public class ReserveStockRequest
{
    [Required]
    public int ProductId { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
}

public class ReleaseReservationRequest
{
    [Required]
    public int ReservationId { get; set; }
}
