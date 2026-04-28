using OrderService.Clients;
using OrderService.DTOs;
using OrderService.Interfaces;
using OrderService.Models;

namespace OrderService.Services;

public class OrderService : IOrderService
{
    private static readonly string[] ValidStatuses = ["Pending", "Processing", "Shipped", "Delivered", "Cancelled"];

    private readonly IOrderRepository _orderRepository;
    private readonly IUserClient _userClient;
    private readonly ICatalogClient _catalogClient;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orderRepository,
        IUserClient userClient,
        ICatalogClient catalogClient,
        ILogger<OrderService> logger)
    {
        _orderRepository = orderRepository;
        _userClient = userClient;
        _catalogClient = catalogClient;
        _logger = logger;
    }

    public async Task<IEnumerable<OrderResponseDto>> GetAllOrdersAsync()
    {
        var orders = await _orderRepository.GetAllAsync();
        return orders.Select(MapToResponseDto);
    }

    public async Task<OrderResponseDto?> GetOrderByIdAsync(int id)
    {
        var order = await _orderRepository.GetByIdAsync(id);
        return order != null ? MapToResponseDto(order) : null;
    }

    public async Task<IEnumerable<OrderResponseDto>> GetOrdersByUserIdAsync(int userId)
    {
        var orders = await _orderRepository.GetByUserIdAsync(userId);
        return orders.Select(MapToResponseDto);
    }

    public async Task<OrderResponseDto> CreateOrderAsync(OrderCreateDto createDto)
    {
        if (!await _userClient.UserExistsAsync(createDto.UserId))
            throw new ArgumentException($"User with ID {createDto.UserId} does not exist.");

        var userSummary = await _userClient.GetUserSummaryAsync(createDto.UserId);

        var orderItems = new List<OrderItem>();
        decimal totalAmount = 0;
        var reservationIds = new List<int>();

        try
        {
            foreach (var itemDto in createDto.OrderItems)
            {
                var productInfo = await _catalogClient.GetProductInfoAsync(itemDto.ProductId);

                var reservationId = await _catalogClient.ReserveStockAsync(itemDto.ProductId, itemDto.Quantity);
                reservationIds.Add(reservationId);

                var subtotal = productInfo.Price * itemDto.Quantity;
                totalAmount += subtotal;

                orderItems.Add(new OrderItem
                {
                    ProductId = itemDto.ProductId,
                    ProductName = productInfo.Name,
                    Quantity = itemDto.Quantity,
                    UnitPrice = productInfo.Price,
                    Subtotal = subtotal
                });
            }

            var order = new Order
            {
                UserId = createDto.UserId,
                UserFirstName = userSummary.FirstName,
                UserLastName = userSummary.LastName,
                ShippingAddress = createDto.ShippingAddress,
                TotalAmount = totalAmount,
                Status = "Pending",
                OrderItems = orderItems
            };

            var created = await _orderRepository.CreateAsync(order);
            _logger.LogInformation("Order {OrderId} created for user {UserId}", created.Id, createDto.UserId);

            return MapToResponseDto(created);
        }
        catch
        {
            foreach (var rid in reservationIds)
            {
                try { await _catalogClient.ReleaseReservationAsync(rid); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to release reservation {ReservationId} during saga compensation", rid);
                }
            }
            throw;
        }
    }

    public async Task<OrderResponseDto?> UpdateOrderAsync(int id, OrderUpdateDto updateDto)
    {
        var existing = await _orderRepository.GetByIdAsync(id);
        if (existing == null) return null;

        if (updateDto.ShippingAddress != null)
            existing.ShippingAddress = updateDto.ShippingAddress;

        if (updateDto.Status != null)
        {
            if (!ValidStatuses.Contains(updateDto.Status))
                throw new ArgumentException($"Invalid status. Valid values are: {string.Join(", ", ValidStatuses)}");

            existing.Status = updateDto.Status;

            if (updateDto.Status == "Shipped" && !existing.ShippedDate.HasValue)
                existing.ShippedDate = DateTime.UtcNow;

            if (updateDto.Status == "Delivered" && !existing.DeliveredDate.HasValue)
                existing.DeliveredDate = DateTime.UtcNow;
        }

        var updated = await _orderRepository.UpdateAsync(existing);
        return updated != null ? MapToResponseDto(updated) : null;
    }

    public async Task<bool> DeleteOrderAsync(int id)
    {
        return await _orderRepository.DeleteAsync(id);
    }

    private static OrderResponseDto MapToResponseDto(Order order) => new()
    {
        Id = order.Id,
        UserId = order.UserId,
        UserName = $"{order.UserFirstName} {order.UserLastName}",
        TotalAmount = order.TotalAmount,
        Status = order.Status,
        ShippingAddress = order.ShippingAddress,
        OrderDate = order.OrderDate,
        ShippedDate = order.ShippedDate,
        DeliveredDate = order.DeliveredDate,
        OrderItems = order.OrderItems.Select(oi => new OrderItemResponseDto
        {
            Id = oi.Id,
            ProductId = oi.ProductId,
            ProductName = oi.ProductName,
            Quantity = oi.Quantity,
            UnitPrice = oi.UnitPrice,
            Subtotal = oi.Subtotal
        }).ToList()
    };
}
