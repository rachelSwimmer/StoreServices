using Microsoft.EntityFrameworkCore;
using ProductCatalogService.Data;
using ProductCatalogService.Interfaces;
using ProductCatalogService.Models;

namespace ProductCatalogService.Repositories;

public class StockReservationRepository : IStockReservationRepository
{
    private readonly CatalogDbContext _context;

    public StockReservationRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public async Task<int> ReserveAsync(int productId, int quantity, TimeSpan ttl)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            var product = await _context.Products.FindAsync(productId);
            if (product == null)
                throw new ArgumentException($"Product with ID {productId} does not exist.");

            if (product.Stock < quantity)
                throw new InvalidOperationException($"Insufficient stock for '{product.Name}'. Available: {product.Stock}");

            product.Stock -= quantity;

            var reservation = new StockReservation
            {
                ProductId = productId,
                Quantity = quantity,
                Status = "Reserved",
                ExpiresAt = DateTime.UtcNow.Add(ttl),
                CreatedAt = DateTime.UtcNow
            };

            _context.StockReservations.Add(reservation);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return reservation.Id;
        });
    }

    public async Task<bool> ReleaseAsync(int reservationId)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            var reservation = await _context.StockReservations
                .Include(r => r.Product)
                .FirstOrDefaultAsync(r => r.Id == reservationId);

            if (reservation == null || reservation.Status == "Released")
                return false;

            reservation.Product.Stock += reservation.Quantity;
            reservation.Status = "Released";

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return true;
        });
    }

    public async Task<bool> ConfirmAsync(int reservationId)
    {
        var reservation = await _context.StockReservations
            .FirstOrDefaultAsync(r => r.Id == reservationId);

        if (reservation == null || reservation.Status != "Reserved")
            return false;

        reservation.Status = "Committed";
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task ReleaseExpiredAsync()
    {
        var expired = await _context.StockReservations
            .Include(r => r.Product)
            .Where(r => r.Status == "Reserved" && r.ExpiresAt < DateTime.UtcNow)
            .ToListAsync();

        foreach (var reservation in expired)
        {
            reservation.Product.Stock += reservation.Quantity;
            reservation.Status = "Released";
        }

        if (expired.Count > 0)
            await _context.SaveChangesAsync();
    }
}
