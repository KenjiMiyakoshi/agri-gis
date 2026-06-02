using System.Text.Json;

namespace AgriGis.Desktop.Dto;

// D401 (WD4): Phase D selection + theme 用 DTO (API 側 SelectionEndpoints/AdminLayerStyleEndpoints と整合)
public sealed record CreateSelectionRequestDto(
    IReadOnlyList<Guid> EntityIds,
    string? ColorHex);

public sealed record CreateSelectionResponseDto(
    Guid Sid,
    string Ttl,
    int Count);

public sealed record LayerStyleDto(JsonElement StyleJson);
