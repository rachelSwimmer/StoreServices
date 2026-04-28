using System.Net.Http.Json;
using BffService.DTOs;

namespace BffService.Clients;

public class CatalogClient : ICatalogClient
{
    private readonly HttpClient _httpClient;

    public CatalogClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ProductStockDto> GetProductStockAsync(int productId)
    {
        var response = await _httpClient.GetAsync($"internal/products/{productId}/price-and-stock");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductStockDto>())!;
    }
}
