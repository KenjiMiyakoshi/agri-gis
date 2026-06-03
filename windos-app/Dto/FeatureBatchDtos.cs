namespace AgriGis.Desktop.Dto;

// E'201 (WE'2) / D'104 (Phase D' WD'1) reuse:
// POST /api/features/batch の WinForms 側 DTO ミラー。

public sealed record FeatureBatchUpdateRequestDto(
    IReadOnlyList<Guid> EntityIds,
    IReadOnlyList<int> IfMatchVersions,
    System.Text.Json.JsonElement AttributesPatch);

public sealed record FeatureBatchUpdateResultDto(
    Guid EntityId,
    int NewVersion,
    DateOnly ValidFrom);

public sealed record FeatureBatchUpdateResponseDto(
    IReadOnlyList<FeatureBatchUpdateResultDto> Results,
    int Count);
