using UserAuthService.DTOs;
using UserAuthService.Interfaces;
using UserAuthService.Models;

namespace UserAuthService.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository userRepository,
        ITokenService tokenService,
        IConfiguration configuration,
        ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _tokenService = tokenService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IEnumerable<UserResponseDto>> GetAllUsersAsync()
    {
        var users = await _userRepository.GetAllAsync();
        return users.Select(MapToResponseDto);
    }

    public async Task<UserResponseDto?> GetUserByIdAsync(int id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        return user != null ? MapToResponseDto(user) : null;
    }

    public async Task<UserResponseDto> CreateUserAsync(UserCreateDto createDto)
    {
        if (await _userRepository.EmailExistsAsync(createDto.Email))
            throw new ArgumentException($"Email {createDto.Email} is already registered.");

        var user = new User
        {
            FirstName = createDto.FirstName,
            LastName = createDto.LastName,
            Email = createDto.Email,
            PasswordHash = HashPassword(createDto.Password),
            Phone = createDto.Phone,
            Address = createDto.Address,
            Role = createDto.Role
        };

        var createdUser = await _userRepository.CreateAsync(user);
        _logger.LogInformation("User created with ID: {UserId}, Role: {Role}", createdUser.Id, createdUser.Role);

        return MapToResponseDto(createdUser);
    }

    public async Task<UserResponseDto?> UpdateUserAsync(int id, UserUpdateDto updateDto)
    {
        var existingUser = await _userRepository.GetByIdAsync(id);
        if (existingUser == null) return null;

        if (updateDto.Email != null && updateDto.Email != existingUser.Email)
        {
            if (await _userRepository.EmailExistsAsync(updateDto.Email))
                throw new ArgumentException($"Email {updateDto.Email} is already registered.");

            existingUser.Email = updateDto.Email;
        }

        if (updateDto.FirstName != null) existingUser.FirstName = updateDto.FirstName;
        if (updateDto.LastName != null) existingUser.LastName = updateDto.LastName;
        if (updateDto.Phone != null) existingUser.Phone = updateDto.Phone;
        if (updateDto.Address != null) existingUser.Address = updateDto.Address;
        if (updateDto.Role.HasValue) existingUser.Role = updateDto.Role.Value;

        var updatedUser = await _userRepository.UpdateAsync(existingUser);
        return updatedUser != null ? MapToResponseDto(updatedUser) : null;
    }

    public async Task<bool> DeleteUserAsync(int id)
    {
        return await _userRepository.DeleteAsync(id);
    }

    public async Task<LoginResponseDto?> AuthenticateAsync(string email, string password)
    {
        var user = await _userRepository.GetByEmailAsync(email);

        if (user == null)
        {
            _logger.LogWarning("Login attempt failed: User not found for email {Email}", email);
            return null;
        }

        if (user.PasswordHash != HashPassword(password))
        {
            _logger.LogWarning("Login attempt failed: Invalid password for email {Email}", email);
            return null;
        }

        var token = _tokenService.GenerateToken(user.Id, user.Email, user.FirstName, user.LastName, user.Role.ToString());
        var expiryMinutes = _configuration.GetValue<int>("JwtSettings:ExpiryMinutes", 60);

        _logger.LogInformation("User {UserId} authenticated successfully", user.Id);

        return new LoginResponseDto
        {
            Token = token,
            TokenType = "Bearer",
            ExpiresIn = expiryMinutes * 60,
            User = MapToResponseDto(user)
        };
    }

    private static UserResponseDto MapToResponseDto(User user)
    {
        return new UserResponseDto
        {
            Id = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Phone = user.Phone,
            Address = user.Address,
            Role = user.Role.ToString(),
            CreatedAt = user.CreatedAt
        };
    }

    private static string HashPassword(string password)
    {
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));
    }
}
