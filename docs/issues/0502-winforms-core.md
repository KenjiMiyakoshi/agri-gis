# 0502: WinForms `Core/` 純粋ロジック

| 項目 | 値 |
|---|---|
| Phase | WinForms |
| Estimate | 1d |
| Depends on | 0501 |
| Blocks | 0503, 0504, 0505 |

## 概要
WinForms 側の純粋ロジック (`AttributeValidator`, `SchemaFormBuilder`, `ProblemDetailsParser`, `ActorContext`, `LayerSchema`) を `Core/` に実装する。**`System.Windows.Forms` を一切参照しない**。

## 背景・目的
案 B' は Core を I/O 無しの純粋ロジックに固定して、xUnit から単体テストできるようにする。フォームから直接バリデーションする実装を許すと CI から動かせなくなる。

## スコープ
### 含む
- `Core/LayerSchema.cs` (record)
  - API の LayerSchemaDto を再表現。`record LayerSchema(IReadOnlyList<SchemaField> Fields)`, `record SchemaField(string Key, string Type, bool Required, string? Label)`
- `Core/AttributeValidator.cs`
  - `Validate(LayerSchema, IReadOnlyDictionary<string, JsonElement>) -> IReadOnlyList<AttributeError>`
  - required / type 検証（API 側 0210 の AttributeValidator と挙動を合わせる）
- `Core/SchemaFormBuilder.cs`
  - `Build(LayerSchema) -> IReadOnlyList<FieldDescriptor>` // UI 生成用の中間表現
  - `record FieldDescriptor(string Key, string Label, FieldKind Kind, bool Required)`
  - **UI コントロールは作らない**。Forms 側で FieldDescriptor を見て生成する
- `Core/ProblemDetailsParser.cs`
  - `Parse(string json) -> ParsedProblem`
  - `record ParsedProblem(int? Status, string? Title, string? RequestId, IReadOnlyList<AttributeError> Errors)`
- `Core/ActorContext.cs`
  - `static class ActorContext { public static string Current { get; } = Environment.UserName; }`
- `Core/AttributeError.cs` (record)

### 含まない
- API クライアント (0503)
- Form / Control (0504)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `Core/` 配下のどのファイルも `System.Windows.Forms` を `using` していない (grep 確認)
- [ ] `AttributeValidator.Validate` が API 側 0210 と同じ判定結果になる
- [ ] `ProblemDetailsParser.Parse` が `extensions.errors` も `errors` (top-level) も両対応
- [ ] `SchemaFormBuilder.Build` が schema を見て FieldDescriptor 配列を返す
- [ ] dotnet build が通る

## 影響ファイル
- `D:\proj\agri-gis\windos-app\Core\LayerSchema.cs` (新規)
- `D:\proj\agri-gis\windos-app\Core\AttributeValidator.cs` (新規)
- `D:\proj\agri-gis\windos-app\Core\AttributeError.cs` (新規)
- `D:\proj\agri-gis\windos-app\Core\SchemaFormBuilder.cs` (新規)
- `D:\proj\agri-gis\windos-app\Core\ProblemDetailsParser.cs` (新規)
- `D:\proj\agri-gis\windos-app\Core\ActorContext.cs` (新規)

## 実装ノート
```csharp
namespace AgriGis.Desktop.Core;

public sealed record SchemaField(string Key, string Type, bool Required, string? Label);
public sealed record LayerSchema(IReadOnlyList<SchemaField> Fields);

public sealed record AttributeError(string AttributeKey, string Code, string Message);

public enum FieldKind { String, Number, Boolean, Unknown }
public sealed record FieldDescriptor(string Key, string Label, FieldKind Kind, bool Required);

public static class SchemaFormBuilder
{
    public static IReadOnlyList<FieldDescriptor> Build(LayerSchema s) =>
        s.Fields.Select(f => new FieldDescriptor(
            f.Key,
            f.Label ?? f.Key,
            f.Type switch
            {
                "string" => FieldKind.String,
                "number" => FieldKind.Number,
                "boolean" => FieldKind.Boolean,
                _ => FieldKind.Unknown
            },
            f.Required
        )).ToArray();
}

public static class AttributeValidator
{
    public static IReadOnlyList<AttributeError> Validate(
        LayerSchema schema, IReadOnlyDictionary<string, JsonElement> attrs)
    {
        // API 側 0210 と同じロジック
    }
}

public static class ProblemDetailsParser
{
    public sealed record ParsedProblem(int? Status, string? Title, string? RequestId, IReadOnlyList<AttributeError> Errors);
    public static ParsedProblem Parse(string json) { /* ... */ }
}

public static class ActorContext
{
    public static string Current { get; } = Environment.UserName;
}
```

注意点:
- `System.Text.Json` は OK（純粋ロジック）
- `ActorContext` は static で読み取りのみ。テストで差し替えたい場合は `Func<string>` にしても OK

## テスト観点
- 0505 で網羅
