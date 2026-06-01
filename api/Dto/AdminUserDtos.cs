namespace AgriGis.Api.Dto;

public sealed record UserDto(
    Guid UserId,
    string LoginId,
    string DisplayName,
    int OrgId,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateUserRequestDto(
    string LoginId,
    string DisplayName,
    int OrgId,
    IReadOnlyList<string> Roles,
    string InitialPassword);

public sealed record UpdateUserRequestDto(
    string? DisplayName,
    int? OrgId,
    IReadOnlyList<string>? Roles);

public sealed record AdminPasswordResetRequestDto(string NewPassword);
