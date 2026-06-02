using AgriGis.Desktop.Dto;

namespace AgriGis.Desktop.Services;

// WB4 B405 (Review② H4 解消): AttributeEditorControl の MainForm 直接キャストを
// インタフェース経由に置換する境界。MainForm が実装し、子コントロールに渡す。
public interface IFeatureSaveCoordinator
{
    Task<PatchFeatureResultDto> UpdateFeatureAsync(
        Guid entityId,
        UpdateFeatureRequestDto req,
        int ifMatchVersion,
        CancellationToken ct);

    Task<FeatureDto> GetFeatureAsync(Guid entityId, CancellationToken ct);
}
