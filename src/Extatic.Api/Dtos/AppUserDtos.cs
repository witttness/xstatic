namespace Extatic.Api.Dtos;

public record AppUserDto(
    Guid Id,
    Guid AppId,
    string Provider,
    string? Email,
    string? DisplayName,
    string? AvatarUrl,
    DateTime? LastLoginAt,
    DateTime CreatedAt);

public record DevTokenRequest(string? Email, string? DisplayName);
public record DevTokenResponse(AppUserDto User, string Token);
