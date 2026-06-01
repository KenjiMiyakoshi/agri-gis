namespace AgriGis.Desktop.Auth;

// ログイン成功後のセッション情報。トークン + ユーザ情報を保持。
// 不変 record。Roles は順序保持のため IReadOnlyList<string>。
public sealed record Session(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    string LoginId,
    string DisplayName,
    int OrgId,
    IReadOnlyList<string> Roles)
{
    // 採択案「案 P」のロール3固定値 (admin / general / guest) に対応する判定
    public bool IsGuest => Roles.Contains("guest");
    public bool IsAdmin => Roles.Contains("admin");
    public bool CanWrite => IsAdmin || Roles.Contains("general");
}
