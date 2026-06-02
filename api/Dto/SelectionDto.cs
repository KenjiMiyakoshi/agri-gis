namespace AgriGis.Api.Dto;

// D202 (WD2): selection endpoint 用 DTO
public sealed record CreateSelectionRequestDto(
    IReadOnlyList<Guid> EntityIds,
    string? ColorHex);

public sealed record CreateSelectionResponseDto(
    Guid Sid,
    string Ttl,    // "session" 固定 (Phase D D102 採用)
    int Count);

public sealed record SelectionInfoDto(
    Guid Sid,
    int Count,
    string ColorHex,
    DateTime CreatedAt);
