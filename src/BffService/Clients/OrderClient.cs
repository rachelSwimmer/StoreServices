using System.Net;
using System.Net.Http.Json;
using BffService.DTOs;

namespace BffService.Clients;

public class OrderClient : IOrderClient
{
    private readonly HttpClient _httpClient;

    public OrderClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OrderDto?> GetOrderAsync(int orderId)
    {
        var response = await _httpClient.GetAsync($"internal/orders/{orderId}");

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderDto>();
    }
}
