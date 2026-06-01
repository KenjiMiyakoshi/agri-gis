using AgriGis.Desktop.Dto;

namespace AgriGis.Desktop.Services;

public interface IApiClient
{
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
        string actor,
        CancellationToken ct);

    Task<PatchFeatureResultDto> UpdateFeatureAsync(
        Guid entityId,
        UpdateFeatureRequestDto req,
        int ifMatchVersion,
        string actor,
        CancellationToken ct);

    Task DeleteFeatureAsync(
        Guid entityId,
        string actor,
        CancellationToken ct);
}
