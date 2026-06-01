namespace AgriGis.Api.Dto;

public sealed record OrgDto(
    int Id,
    string Name,
    string Code,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateOrgRequestDto(string Name, string Code);

public sealed record UpdateOrgRequestDto(string? Name, string? Code);
