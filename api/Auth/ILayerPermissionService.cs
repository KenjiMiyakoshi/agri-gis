namespace AgriGis.Api.Auth;

// F204 (Phase F WF2): 組織×レイヤ権限の判定サービス。
// org_layer_permission テーブルを参照。
// admin role 持ちは全 layer に対し can_view=true / can_edit=true (filter bypass)。
public interface ILayerPermissionService
{
    Task<bool> CanViewAsync(int orgId, int layerId, IReadOnlyList<string> roles, CancellationToken ct);
    Task<bool> CanEditAsync(int orgId, int layerId, IReadOnlyList<string> roles, CancellationToken ct);

    // F203: 管理画面 (組織選択 → 全 layer 権限一覧) で使用。
    // admin role でも層自体の絞り込みは行わない (全 layer に対する org の権限を返す)。
    Task<IReadOnlyDictionary<int, (bool CanView, bool CanEdit)>> GetForOrgAsync(int orgId, CancellationToken ct);
}
