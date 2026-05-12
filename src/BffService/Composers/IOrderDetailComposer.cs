using BffService.DTOs;

namespace BffService.Composers;

public interface IOrderDetailComposer
{
    Task<OrderDetailView?> ComposeAsync(int orderId, CancellationToken ct);
}
