using AgriGis.Desktop.Dto;

namespace AgriGis.Desktop.Services;

public interface IApiClient
{
    Task<LoginResponseDto> LoginAsync(string loginId, string password, CancellationToken ct);

    Task<IReadOnlyList<LayerDto>> GetLayersAsync(CancellationToken ct);

    Task<LayerSchemaResponseDto> GetLayerSchemaAsync(int layerId, CancellationToken ct);

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
    Task<IReadOnlyList<LayerAdminDto>> ListLayersAdminAsync(bool includeDeleted, CancellationToken ct);
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
    Task<LayerStyleDto> GetLayerStyleAsync(int layerId, CancellationToken ct);
    Task<LayerStyleDto> UpdateLayerStyleAsync(int layerId, LayerStyleDto style, CancellationToken ct);
}
