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
}
