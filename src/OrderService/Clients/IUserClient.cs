namespace OrderService.Clients;

public interface IUserClient
{
    Task<bool> UserExistsAsync(int userId);
    Task<UserSummaryDto> GetUserSummaryAsync(int userId);
}

public class UserSummaryDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
