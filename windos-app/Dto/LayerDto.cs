namespace AgriGis.Desktop.Dto;

// API record と命名一致 (camelCase JSON は JsonSerializerOptions で対応)
// F201 (Phase F WF2): CanEdit を追加。WinForms 側で AttributeEditor の read-only 制御に使う。
//   - JSON 互換: 古い API (canEdit 無し) からのレスポンスは canEdit=false にデシリアライズされる
//     (API 側は Phase F 以降 canEdit を必ず返すので実害なし)
public sealed record LayerDto(
    int LayerId,
    string LayerName,
    string LayerType,
    int? OwnerOrgId,
    bool IsShared,
    DateTimeOffset CreatedAt,
    int SchemaVersion,
    LayerSchemaDto Schema,
    bool CanEdit = false
);

public sealed record LayerSchemaDto(IReadOnlyList<SchemaFieldDto> Fields);

public sealed record SchemaFieldDto(string Key, string Type, bool Required, string? Label);

public sealed record LayerSchemaResponseDto(
    int LayerId,
    int SchemaVersion,
    LayerSchemaDto Schema
);
