using AgriGis.Desktop.Dto;

namespace AgriGis.Desktop.Services;

public interface IApiClient
{
    Task<LoginResponseDto> LoginAsync(string loginId, string password, CancellationToken ct);

    // E'201 (WE'2): asOf 引数追加 (DateOnly? YYYY-MM-DD)
    Task<IReadOnlyList<LayerDto>> GetLayersAsync(DateOnly? asOf, CancellationToken ct);

    Task<LayerSchemaResponseDto> GetLayerSchemaAsync(int layerId, DateOnly? asOf, CancellationToken ct);

    // D205 (WD2): GetFeaturesAsync は Phase D で削除。
    // Phase A/B/C 期に追加された全件 GeoJSON 取得経路は Phase D で TileLayer に切替。
    // 編集モード時は GetFeatureAsync (単一 entity) のみ使用。

    Task<FeatureDto> GetFeatureAsync(
        Guid entityId,
        DateOnly? asOf,
        CancellationToken ct);

    Task<CreateFeatureResultDto> CreateFeatureAsync(
        CreateFeatureRequestDto req,
        CancellationToken ct);

    Task<PatchFeatureResultDto> UpdateFeatureAsync(
        Guid entityId,
        UpdateFeatureRequestDto req,
        int ifMatchVersion,
        CancellationToken ct);

    Task DeleteFeatureAsync(
        Guid entityId,
        CancellationToken ct);

    // WB3 B401: AdminLayers CRUD
    // E'201 (WE'2): asOf 指定時は includeDeleted 無視 (asOf 時点の active layer のみ返却)
    Task<IReadOnlyList<LayerAdminDto>> ListLayersAdminAsync(bool includeDeleted, DateOnly? asOf, CancellationToken ct);
    Task<LayerAdminDto> CreateLayerAsync(CreateLayerRequestDto req, CancellationToken ct);
    Task<LayerAdminDto> UpdateLayerAsync(int layerId, UpdateLayerRequestDto req, CancellationToken ct);
    Task DeleteLayerAsync(int layerId, CancellationToken ct);

    // WB3 B401: Import jobs + bulk
    Task<ImportJobDto> StartImportJobAsync(int layerId, StartImportJobRequestDto req, CancellationToken ct);
    Task<ImportJobDto> GetImportJobAsync(Guid jobId, CancellationToken ct);
    Task<ImportJobDto> FinalizeImportJobAsync(Guid jobId, FinalizeImportJobRequestDto req, CancellationToken ct);
    Task<BulkFeaturesResponseDto> BulkInsertFeaturesAsync(int layerId, BulkFeaturesRequestDto req, CancellationToken ct);

    // D401 (WD4): Phase D 新エンドポイント
    Task<CreateSelectionResponseDto> CreateSelectionAsync(
        IReadOnlyList<Guid> entityIds, string? colorHex, CancellationToken ct);
    Task DeleteSelectionAsync(Guid sid, CancellationToken ct);
    Task LogoutAsync(CancellationToken ct);
    Task<LayerStyleDto> GetLayerStyleAsync(int layerId, DateOnly? asOf, CancellationToken ct);
    Task<LayerStyleDto> UpdateLayerStyleAsync(int layerId, LayerStyleDto style, CancellationToken ct);

    // E'201 (WE'2) / D'104 reuse: POST /api/features/batch
    Task<FeatureBatchUpdateResponseDto> BatchUpdateFeaturesAsync(
        FeatureBatchUpdateRequestDto req, CancellationToken ct);

    // F306 (Phase F WF3): 組織一覧 + 組織×レイヤ権限管理
    Task<IReadOnlyList<OrgDto>> ListOrgsAsync(CancellationToken ct);
    Task<IReadOnlyList<OrgLayerPermissionDto>> GetOrgLayerPermissionsAsync(int orgId, CancellationToken ct);
    Task<IReadOnlyList<OrgLayerPermissionDto>> UpdateOrgLayerPermissionsAsync(
        int orgId, OrgLayerPermsUpsertDto req, CancellationToken ct);
}
