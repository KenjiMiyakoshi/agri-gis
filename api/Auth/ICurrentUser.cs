namespace AgriGis.Api.Auth;

// リクエストスコープのカレントユーザ。HttpContext.User の claims から導出される。
// 認可不要なエンドポイント (login, health) では認証 claims が無いため、
// IsAuthenticated=false かつ各プロパティは "anonymous" 相当を返す。
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    string LoginId { get; }
    string DisplayName { get; }
    int OrgId { get; }
    IReadOnlyList<string> Roles { get; }
    bool HasRole(string role);

    // D103 (WD1): JWT に詰めた sid_session claim から導出。
    // user_sessions.session_id と紐付き、logout で deleted_at が埋まると次回 401。
    Guid SessionId { get; }
}
