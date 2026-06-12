using System.Text.Json;

namespace AgriGis.Desktop.Dto;

// F'306 (Phase F' WF'3): API record と命名一致
public sealed record UserPreferenceDto(
    string Key,
    JsonElement Value,
    DateTimeOffset UpdatedAt);

public sealed record UserPreferencePutDto(
    JsonElement Value);
