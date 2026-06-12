using System.Text.Json;

namespace AgriGis.Api.Dto;

// F'302 (Phase F' WF'3): /api/user/preferences/{key} の応答 + PUT body
//   GET → 200 PreferenceDto / 404 not found
//   PUT → 200 PreferenceDto (upsert 後の値)
public sealed record UserPreferenceDto(
    string Key,
    JsonElement Value,
    DateTimeOffset UpdatedAt);

public sealed record UserPreferencePutDto(
    JsonElement Value);
