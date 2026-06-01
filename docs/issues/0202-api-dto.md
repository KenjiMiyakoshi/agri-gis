# 0202: DTO 定義 (record) と JSON 設定

| 項目 | 値 |
|---|---|
| Phase | API |
| Estimate | 0.5d |
| Depends on | 0201 |
| Blocks | 0205, 0208, 0209, 0210, 0211, 0212, 0402 |

## 概要
API のリクエスト / レスポンスを `record` で明示定義する。`JsonObject` や匿名型は使わない。

## 背景・目的
案 B' は WebGIS / WinForms の両方が API を消費する。型のシリアライズ形が暗黙的だと衝突するので、**record DTO** で固定する。命名は WebGIS の TypeScript 型と一致させる。

## スコープ
### 含む
- `api/Dto/` フォルダ
- レイヤ系: `LayerDto`, `LayerSchemaDto`, `SchemaFieldDto`, `UpdateSchemaRequestDto`
- フィーチャ系: `FeatureDto` (GeoJSON Feature 風 with `featureId`/`entityId`/`layerId`/`version`/`validFrom`/`validTo`), `FeatureCollectionDto`, `CreateFeatureRequestDto`, `UpdateFeatureRequestDto`, `FeatureHistoryDto`
- エラー系: `ProblemDetailsExtensionDto` (`errors[]` の各要素 `{ attributeKey, code, message }`)
- JSON シリアライズオプション: camelCase, null 無視（任意）、`DateOnly` を ISO-8601 で扱う
- `Program.cs` に `builder.Services.ConfigureHttpJsonOptions(...)` で適用

### 含まない
- 各エンドポイントの実装（後続イシューで使う）
- バリデーション属性

## 受け入れ条件 (Acceptance Criteria)
- [ ] `dotnet build` が通る
- [ ] 既存 `GET /api/layers` を DTO 経由に書き換えても従来のレスポンス JSON 構造（プロパティ名 camelCase）を維持
- [ ] `DateOnly` が `"2026-05-29"` 形式でシリアライズされる
- [ ] DTO に `JsonObject` や匿名型を使っていない（grep で確認）

## 影響ファイル
- `D:\proj\agri-gis\api\Dto\LayerDto.cs` (新規)
- `D:\proj\agri-gis\api\Dto\LayerSchemaDto.cs` (新規)
- `D:\proj\agri-gis\api\Dto\FeatureDto.cs` (新規)
- `D:\proj\agri-gis\api\Dto\FeatureCollectionDto.cs` (新規)
- `D:\proj\agri-gis\api\Dto\CreateFeatureRequestDto.cs` (新規)
- `D:\proj\agri-gis\api\Dto\UpdateFeatureRequestDto.cs` (新規)
- `D:\proj\agri-gis\api\Dto\FeatureHistoryDto.cs` (新規)
- `D:\proj\agri-gis\api\Dto\ProblemDetailsExtensionDto.cs` (新規)
- `D:\proj\agri-gis\api\Endpoints\LayerEndpoints.cs` (DTO 化)
- `D:\proj\agri-gis\api\Endpoints\FeatureEndpoints.cs` (DTO 化)
- `D:\proj\agri-gis\api\Program.cs` (JSON オプション設定)

## 実装ノート
```csharp
// Dto/LayerDto.cs
namespace AgriGis.Api.Dto;
public sealed record LayerDto(
    int LayerId,
    string LayerName,
    string LayerType,
    int? OwnerOrgId,
    bool IsShared,
    DateTimeOffset CreatedAt,
    int SchemaVersion,
    LayerSchemaDto Schema
);

// Dto/LayerSchemaDto.cs
public sealed record LayerSchemaDto(IReadOnlyList<SchemaFieldDto> Fields);
public sealed record SchemaFieldDto(string Key, string Type, bool Required, string? Label);

// Dto/FeatureDto.cs (GeoJSON Feature を踏襲)
public sealed record FeatureDto(
    string Type,                 // "Feature" 固定
    JsonElement Geometry,        // GeoJSON geometry
    FeaturePropertiesDto Properties
);
public sealed record FeaturePropertiesDto(
    long FeatureId,
    int LayerId,
    string EntityId,
    int Version,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    int AttributesSchemaVersion,
    string CreatedBy,
    string UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Dictionary<string, JsonElement> Attributes
);

// Dto/FeatureCollectionDto.cs
public sealed record FeatureCollectionDto(
    string Type,                 // "FeatureCollection"
    CrsDto Crs,
    IReadOnlyList<FeatureDto> Features
);
public sealed record CrsDto(string Type, CrsPropertiesDto Properties);
public sealed record CrsPropertiesDto(string Name);

// Dto/CreateFeatureRequestDto.cs
public sealed record CreateFeatureRequestDto(
    int LayerId,
    JsonElement Geometry,        // GeoJSON 4326
    Dictionary<string, JsonElement> Attributes
);

// Dto/UpdateFeatureRequestDto.cs (null は据え置きの意味)
public sealed record UpdateFeatureRequestDto(
    JsonElement? Geometry,
    Dictionary<string, JsonElement>? Attributes
);

// Dto/ProblemDetailsExtensionDto.cs
public sealed record AttributeErrorDto(string AttributeKey, string Code, string Message);
```

```csharp
// Program.cs
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
```

注意点:
- `JsonElement` を使うのは GeoJSON geometry や属性辞書のように構造が可変だから。`JsonObject` は使わない
- 属性辞書を `Dictionary<string, JsonElement>` にすると、後段で `JsonValueKind` で型判別できる

## テスト観点
- 0301: `GET /api/layers` のレスポンス JSON プロパティ名が camelCase
- 0301: DTO が DB の `schema_json` を `LayerSchemaDto` に正しくマップ
