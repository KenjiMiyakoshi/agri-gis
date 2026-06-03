# ImportWizard Required トグル UI (Phase C' C'6)

ImportWizard Step2 SchemaGrid で `Required` 列を編集可能にし、`GdalInferenceStrategy` の自動推論を上書きできるようにする。

## 背景

Phase C PR #167 で `GdalInferenceStrategy` の `sampleCoversAll=false` (= 実件数 > SampleSize) 時に `Nullable=true / Required=false` を保守的に返すよう修正した。これにより 10000 件超 SHP で sample 外に空値が潜むケースの 422 を回避。

しかし、ユーザが「データは欠損ありえないので Required=true で投入したい」場合に手動上書きできない。SchemaGrid は Required CheckBox 列を持つが、`ReadOnly=true` で固定。

メモリ `import_wizard_required_toggle.md` で本要件が言及されている (Phase C smoke test 後にユーザ要望)。

## 採用方針

### SchemaGrid の編集可化

```csharp
// SchemaGrid.cs
requiredColumn.ReadOnly = false;
nullableColumn.ReadOnly = false;
```

ただし「自動推論」と「ユーザ上書き」を識別するため、ViewModel に `RequiredOverridden: bool` フラグ追加。

### ViewModel 拡張

```csharp
public sealed class SchemaFieldRow
{
    public string Key { get; set; } = "";
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public bool Nullable { get; set; }
    public string? Label { get; set; }
    // C'303 (WC'3): 自動推論を上書きしたかどうか
    public bool RequiredOverridden { get; set; }

    // GdalInferenceStrategy が出した「元」値 (audit 用)
    public bool InferredRequired { get; set; }
}
```

Required CheckBox の編集イベント:
```csharp
private void OnRequiredCellEditEnded(int rowIdx)
{
    var row = _rows[rowIdx];
    if (row.Required != row.InferredRequired)
    {
        row.RequiredOverridden = true;
    }
    UpdateOverrideIndicator(rowIdx);
}
```

### UI インジケータ

- `RequiredOverridden=true` のセルに ⚠ アイコン or 背景色 (淡黄)
- ツールチップ: 「自動推論では Nullable (sample 外に空値の可能性)。手動で Required=ON に上書き中。空値があると API が 422 を返します」

### ImportAsync への伝搬

```csharp
var schemaFields = _rows.Select(r => new SchemaFieldDto(
    Key: r.Key,
    Type: r.Type,
    Required: r.Required,
    Label: r.Label)).ToList();
```

`RequiredOverridden` フラグは API には送らない (UI 内部状態)。audit ログには現状送らない (Phase C''  候補)。

### 「全フィールド一括 ON/OFF」UI

**Phase C'' 送り** (現状は 1 行ずつ編集)。

## 422 エラー時の UX

ユーザが Required=true に上書き → 投入 → API が 422 を返したとき:
- エラー詳細を Step3 のエラーダイアログに表示
- 「Step2 に戻る」ボタン → 該当フィールドの Required を視覚的に強調 (赤背景)
- ユーザは Required=false に戻して再投入

## 受入条件

1. SchemaGrid の Required / Nullable 列が編集可
2. 手動上書き時に ⚠ アイコン or 背景色変化 + ツールチップ表示
3. ImportAsync が SchemaFieldDto.Required にユーザ指定値を反映
4. 422 受領時に Step2 に戻り、該当フィールドを強調表示
5. `SchemaGridRequiredOverrideTests` (ViewModel level) で:
   - 自動推論 Required=false の行を手動 true に → `RequiredOverridden=true`
   - 自動推論 Required=true の行を変更しない → `RequiredOverridden=false`
   - ImportAsync 直前の SchemaFieldDto.Required がユーザ値

## テスト

`SchemaGridRequiredOverrideTests` (windos-app.tests):
- ViewModel level (Form は使わない、`SchemaFieldRow` の状態遷移のみ)
- 「自動推論で Required=false」+「ユーザが Required=true に変更」→ `RequiredOverridden=true` フラグ確認
- ImportAsync の引数として渡される SchemaFieldDto.Required がユーザ値

UI 自体の操作テストは Form テストインフラが Phase C' 範囲外のため省略。

## 関連

- `PHASE_C_PRIME_INDEX.md`
- メモリ `import_wizard_required_toggle.md`
- `windos-app/Controls/SchemaGrid.cs`
- `windos-app/ViewModels/ImportWizardViewModel.cs`
- PR #167 (`GdalInferenceStrategy` の sample 外保守化)
