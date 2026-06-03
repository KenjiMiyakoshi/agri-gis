using System.Text.Json;

namespace AgriGis.Api.Dto;

// D'104 (WD'1): POST /api/features/batch 用 DTO。
// all-or-nothing 楽観ロック、属性のみ patch (geometry は不変)。

public sealed record FeatureBatchUpdateRequestDto(
    IReadOnlyList<Guid> EntityIds,
    IReadOnlyList<int> IfMatchVersions,
    JsonElement AttributesPatch);

public sealed record FeatureBatchUpdateResultDto(
    Guid EntityId,
    int NewVersion,
    DateOnly ValidFrom);

public sealed record FeatureBatchUpdateResponseDto(
    IReadOnlyList<FeatureBatchUpdateResultDto> Results,
    int Count);
