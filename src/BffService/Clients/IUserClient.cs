using BffService.DTOs;

namespace BffService.Clients;

public interface IUserClient
{
    Task<UserSummaryDto> GetUserSummaryAsync(int userId);
}
