using BffService.DTOs;

namespace BffService.Clients;

public interface IOrderClient
{
    Task<OrderDto?> GetOrderAsync(int orderId);
}
