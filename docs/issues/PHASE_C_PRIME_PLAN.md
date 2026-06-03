# Phase C' Plan — 課題分析と採用案

Phase C' 着手前の課題分析と採用案。Phase D'/E' 流儀踏襲。

## 0. 出発点

Phase C 本体 (Shapefile + GDAL) 完了時に `PHASE_C_DESIGN_P §6.12` で明示した 6 件:

1. MIF/MID 対応 (1.5d)
2. TAB 対応 (1.5d)
3. `IImportPackage` 抽象切り出し (1.0d、3 形式実装後)
4. 和歌山旧測地系 WKT 収録 (1.5d、`SridConverter.RegisterWkt` は Phase C で公開)
5. `UcsDetectResolver` 実装 (0.8d)
6. ImportWizard Required トグル UI

Phase C で「API は公開、実装は埋めない」スタンスを取ったため、Phase C' は **公開済 API を本体で埋める** サイクルになる。新規設計はほぼ無く、Design は「ヘッダ仕様 + ファイル構造 + 信頼度しきい値」の整理が主体。

## 1. 課題 1: MIF/MID 対応

### 1.1 現状

- `GdalLayerSource.cs` の driver switch は `"mif" → "MapInfo File"` を含む (Phase C WC2 で先行配置)
- ただし `GdalLayerSource` は `ShapefilePackage` しか受け取れない (`_package.ShpPath` 直参照)
- ImportWizard Step1 の FormatItems で MIF は "Phase C' 対応予定" 表示

### 1.2 採用案

**案 A: `MifPackage : IImportPackage` 新規 + `IImportPackage` 抽象切り出し**

- `MifPackage`: zip 展開 → `.mif` + `.mid` セット確認 → `PrimaryPath = mif` を返す
- `.mif` ヘッダ先頭の `CharSet "WindowsLatin1"` 等を 1 行だけ抽出 (full parser は不要、driver 任せ)
- `CoordSys` 行も同様に抽出して `SridDetector` の補助に渡す

落選:
- `MifPackage` を `ShapefilePackage` 派生にする: sidecar 構成が違うため継承で表現するのは過剰

### 1.3 影響範囲

- 新規: `MifPackage.cs`, `IImportPackage.cs`, `MifPackageTests.cs`
- 変更: `ShapefilePackage.cs` (`IImportPackage` 実装)、`GdalLayerSource.cs` (`_package.PrimaryPath`)、`ImportWizardForm.cs` (Step1 解禁)、`ImportWizardViewModel.cs` (`CreateSourceAsync` の switch)

## 2. 課題 2: TAB 対応

### 2.1 現状

`GdalLayerSource` の driver switch には `"tab" → "MapInfo File"` も既存 (同 driver を共有)。TabPackage 未実装、Step1 で "Phase C' 対応予定" 表示。

### 2.2 採用案

**案 A: `TabPackage : IImportPackage` 新規**

- TAB は **5 ファイルセット** (.tab + .map + .dat + .id) + 任意 .ind
- `.tab` ヘッダの `CharSet "..."` + `CoordSys Earth Projection ...` 行を抽出
- CoordSys が EPSG コードを直接持たないケース (和歌山旧測地系) は `SridCatalogBootstrapper` で事前登録された WKT と照合 (3 で扱う)

### 2.3 影響範囲

- 新規: `TabPackage.cs`, `TabPackageTests.cs`
- 変更: `ImportWizardForm.cs` (Step1 解禁)、`ImportWizardViewModel.cs` (switch)

## 3. 課題 3: `IImportPackage` 抽象切り出し

### 3.1 採用案

**最小 API 宣言で開始**:

```csharp
public interface IImportPackage : IAsyncDisposable
{
    string PrimaryPath { get; }       // SHP / MIF / TAB の主ファイル
    IReadOnlyList<string> MissingOptional { get; }  // .cpg や .ind 等の任意 sidecar
}
```

`ShapefilePackage.ShpPath` プロパティは public で残置 (既存テスト + GdalLayerSource が両方プロパティを使う段階移行を許す)。WC'1 末で `PrimaryPath` に切り替えて完了。

落選:
- `IImportPackage` に `Encoding`, `Srid`, `Driver` 等をフル定義: 各 Package が知らない情報まで要求してしまう。これらは `IEncodingResolver` / `ISridDetector` の責務

## 4. 課題 4: 和歌山旧測地系 SridCatalog

### 4.1 現状

- `SridConverter.RegisterWkt(int srid, string wkt)` は Phase C で公開済
- 和歌山旧測地系系 II (旧日本測地系 zone 2) などは EPSG コードを持たないため、SRID は **ローカル ID (例: 99001)** で割り当てる

### 4.2 採用案

**案 A: `Import:SridCatalog[]` で起動時 RegisterWkt**

```json
{
  "Import": {
    "SridCatalog": [
      {
        "Srid": 99001,
        "Name": "旧日本測地系 平面直角座標系 II 系 (和歌山)",
        "Wkt": "PROJCS[\"...\", GEOGCS[...], ...]",
        "Source": "https://www.gsi.go.jp/..."
      }
    ]
  }
}
```

- `SridCatalogBootstrapper` が起動時に `RegisterWkt` を一括呼び出し
- TAB の `CoordSys` 行から detector が WKT を生成 → `SridConverter` で既知 WKT と照合 → SRID 解決

### 4.3 影響範囲

- 新規: `SridCatalogBootstrapper.cs`, `SridCatalogBootstrapperTests.cs`
- 変更: `ImportOptions.cs` (`SridCatalog: List<SridCatalogEntry>`)、`appsettings.json` (旧日本測地系 2 件)、`Program.cs` (起動時呼び出し)

## 5. 課題 5: UcsDetectResolver

### 5.1 現状

- `IEncodingResolver` interface 公開済、`CpgFileResolver` のみ実装 (`.cpg` ファイル優先)
- UCSDet 系自動検出は Phase C で「不採用」と明示、Phase C' 候補に

### 5.2 採用案

**案 A: NuGet `UtfUnknown` + 信頼度 ≥ 0.7 で採用**

- `.dbf` 先頭 4096 バイトを `CharsetDetector.DetectFromBytes` で判定
- 結果が `Confidence >= 0.7` なら採用、未満は `CpgFileResolver` にフォールバック
- MIF/TAB の `CharSet` ヘッダは別途 `MifTabCharSetParser` で WindowsLatin1 / EUCJP / CP949 等を正規化

落選:
- Mozilla Universal Chardet (`UDE.NETStandard`): NuGet が長期メンテナンスされていない
- 自前実装: 投資対効果に合わない

### 5.3 影響範囲

- 新規: `UcsDetectResolver.cs`, `MifTabCharSetParser.cs`, `UcsDetectResolverTests.cs`
- 変更: `Program.cs` (UcsDetect → CpgFile → Default の chain 登録)、`csproj` (NuGet `UtfUnknown` 追加)

## 6. 課題 6: ImportWizard Required トグル UI

### 6.1 現状

- `GdalInferenceStrategy` は実件数 > SampleSize の時 `Nullable=true / Required=false` を保守的に返す (PR #167 で実装)
- SchemaGrid は Required CheckBox 列を持つが、Nullable 列が編集可になっていない
- ユーザが「本当は Required にしたい」場合に上書きできない

### 6.2 採用案

**案 A: Step2 SchemaGrid で Required を編集可 + `RequiredOverridden` フラグ + ツールチップで自動推論の根拠表示**

- SchemaGrid.Required 列を編集可
- ViewModel に `RequiredOverridden: bool` 列追加 (Audit 用)
- ユーザが手動 Required=true に変更 → 「⚠ 自動推論 (sample 外に空値の可能性) を上書き中」インジケータ表示
- ImportAsync → API request の `SchemaFieldDto.Required` に反映

### 6.3 影響範囲

- 変更: `SchemaGrid.cs` (Required 列の `ReadOnly = false`)、`ImportWizardViewModel.cs` (`RequiredOverridden` フラグ + ImportAsync 経路)
- 新規テスト: `SchemaGridRequiredOverrideTests`

## 7. 残課題 (Phase C'' 候補)

- KML / KMZ インポート
- DXF / DGN CAD 形式
- GeoPackage 出力
- TAB のフォルダ選択経路 (zip 不要)
- UcsDetect 信頼度しきい値の admin 画面設定
- Required トグルの「全フィールド一括 ON/OFF」
- `SridCatalog` の DB 永続化

## 関連

- `PHASE_C_PRIME_INDEX.md`
- `PHASE_C_PRIME_WAVE_PLAN.md`
- `PHASE_C_PRIME_ISSUES_INDEX.md`
