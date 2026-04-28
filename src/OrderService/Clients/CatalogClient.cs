using System.Net;
using System.Net.Http.Json;

namespace OrderService.Clients;

public class CatalogClient : ICatalogClient
{
    private readonly HttpClient _httpClient;

    public CatalogClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ProductInfoDto> GetProductInfoAsync(int productId)
    {
        var response = await _httpClient.GetAsync($"internal/products/{productId}/price-and-stock");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ArgumentException($"Product with ID {productId} does not exist.");

        response.EnsureSuccessStatusCode();
        var info = await response.Content.ReadFromJsonAsync<ProductInfoDto>();
        return info!;
    }

    public async Task<int> ReserveStockAsync(int productId, int quantity)
    {
        var response = await _httpClient.PostAsJsonAsync("internal/products/reserve-stock",
            new { productId, quantity });

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ArgumentException($"Product with ID {productId} does not exist.");

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            throw new InvalidOperationException(error?.Message ?? "Insufficient stock.");
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ReserveStockResponse>();
        return result!.ReservationId;
    }

    public async Task ReleaseReservationAsync(int reservationId)
    {
        await _httpClient.PostAsJsonAsync("internal/products/release-reservation",
            new { reservationId });
    }

    private class ReserveStockResponse
    {
        public int ReservationId { get; set; }
    }

    private class ErrorResponse
    {
        public string Message { get; set; } = string.Empty;
    }
}
