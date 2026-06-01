namespace AgriGis.Desktop.Dto;

public sealed record LoginRequestDto(string LoginId, string Password);

public sealed record LoginResponseDto(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    UserInfoDto User);

public sealed record UserInfoDto(
    Guid UserId,
    string LoginId,
    string DisplayName,
    int OrgId,
    IReadOnlyList<string> Roles);
