namespace AgriGis.Api.Auth;

// D103 (WD1): JWT lifecycle と紐付く user_sessions テーブルへの DB アクセスを抽象化する。
//
// 設計判断 (PHASE_D_DESIGN_P §2.4):
//   - JWT 発行 (login) で CreateSessionAsync (user_sessions INSERT、sid を返す)
//   - JwtBearer.OnTokenValidated で IsActiveAsync (deleted_at IS NULL を確認)
//   - logout (D204) で InvalidateSessionAsync (deleted_at = now())
//   - selection_sets.session_id FK CASCADE で関連する選択集合は自動削除
public interface IUserSessionStore
{
    /// <summary>
    /// 新規セッションを作成し session_id を返す。jwt_jti は JWT の jti claim を格納。
    /// </summary>
    Task<Guid> CreateSessionAsync(Guid userId, string jwtJti, CancellationToken ct);

    /// <summary>
    /// セッションを論理削除する (deleted_at = now())。冪等。
    /// </summary>
    Task InvalidateSessionAsync(Guid sessionId, CancellationToken ct);

    /// <summary>
    /// セッションが active か (= deleted_at IS NULL) を返す。
    /// JwtBearer.OnTokenValidated で毎リクエスト呼ばれるためインデックス前提。
    /// </summary>
    Task<bool> IsActiveAsync(Guid sessionId, CancellationToken ct);
}
