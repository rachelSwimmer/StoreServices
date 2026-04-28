namespace ProductCatalogService.Interfaces;

public interface IStockReservationRepository
{
    Task<int> ReserveAsync(int productId, int quantity, TimeSpan ttl);
    Task<bool> ReleaseAsync(int reservationId);
    Task ReleaseExpiredAsync();
}
