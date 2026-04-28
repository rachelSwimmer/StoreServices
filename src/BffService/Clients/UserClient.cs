using System.Net.Http.Json;
using BffService.DTOs;

namespace BffService.Clients;

public class UserClient : IUserClient
{
    private readonly HttpClient _httpClient;

    public UserClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<UserSummaryDto> GetUserSummaryAsync(int userId)
    {
        var response = await _httpClient.GetAsync($"internal/users/{userId}/summary");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserSummaryDto>())!;
    }
}
