using AgriGis.Desktop.Dto;

namespace AgriGis.Desktop.Services;

public interface IApiClient
{
    Task<LoginResponseDto> LoginAsync(string loginId, string password, CancellationToken ct);

    Task<IReadOnlyList<LayerDto>> GetLayersAsync(CancellationToken ct);

    Task<LayerSchemaResponseDto> GetLayerSchemaAsync(int layerId, CancellationToken ct);

    Task<FeatureCollectionDto> GetFeaturesAsync(
        int layerId,
        DateOnly? asOf,
        CancellationToken ct);

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
}
