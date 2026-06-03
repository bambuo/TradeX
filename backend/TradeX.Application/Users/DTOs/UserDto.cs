namespace TradeX.Application.Users.DTOs;

public sealed record UserDto(
    Guid Id,
    string Username,
    string Email,
    string Role,
    string Status,
    DateTime CreatedAt,
    DateTime? LastLoginAt);
