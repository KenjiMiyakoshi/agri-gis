namespace AgriGis.Api.Dto;

public sealed record LoginRequestDto(string LoginId, string Password);

public sealed record LoginResponseDto(
    string AccessToken,
    DateTime ExpiresAt,
    UserInfoDto User);

public sealed record UserInfoDto(
    Guid UserId,
    string LoginId,
    string DisplayName,
    int OrgId,
    IReadOnlyList<string> Roles);

public sealed record ChangePasswordRequestDto(string CurrentPassword, string NewPassword);
