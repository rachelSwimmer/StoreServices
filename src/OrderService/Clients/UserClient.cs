using System.Net;
using System.Net.Http.Json;

namespace OrderService.Clients;

public class UserClient : IUserClient
{
    private readonly HttpClient _httpClient;

    public UserClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool> UserExistsAsync(int userId)
    {
        var response = await _httpClient.GetAsync($"internal/users/{userId}/exists");
        return response.StatusCode == HttpStatusCode.OK;
    }

    public async Task<UserSummaryDto> GetUserSummaryAsync(int userId)
    {
        var response = await _httpClient.GetAsync($"internal/users/{userId}/summary");
        response.EnsureSuccessStatusCode();

        var summary = await response.Content.ReadFromJsonAsync<UserSummaryDto>();
        return summary!;
    }
}
