# Phase C' Issues Index

Phase C' で起票する全 16 Issue の一覧。

ラベル: `phase:C-prime`, `wave:WC'N`, `area:db|api|webgis|winforms|tests|docs`

## WC'0 — Plan + Design

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| C'100 | Phase C' Plan + Design 4 本 + Index 作成 | docs | 0.5d |

## WC'1 — IImportPackage + MIF/MID

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| C'101 | `IImportPackage` interface + `ShapefilePackage` 実装書き換え (ShpPath 残置で後方互換) | winforms | 0.5d |
| C'102 | `MifPackage : IImportPackage` 新規 + `.mif` ヘッダ CharSet 抽出 | winforms | 0.7d |
| C'103 | `GdalLayerSource` driver switch 整理 + `_package.PrimaryPath` 参照に変更 | winforms | 0.5d |
| C'104 | ImportWizard Step1 MIF/MID 解禁 (FormatItems 修正 + フィルタ) | winforms | 0.3d |
| C'105 | `MifPackageTests` + `GdalLayerSourceContractTests` MIF サンプル | tests | 0.5d |

## WC'2 — TAB + SridCatalog

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| C'201 | `TabPackage : IImportPackage` 新規 (.tab + .map + .dat + .id 必須、.ind 任意、CharSet/CoordSys 抽出) | winforms | 0.7d |
| C'202 | `ImportOptions.SridCatalog[]` + `SridCatalogEntry` + `SridCatalogBootstrapper` 起動時 RegisterWkt | winforms | 0.4d |
| C'203 | `appsettings.json` の `Import:SridCatalog[]` に和歌山旧測地系 2 件 + 出典 | winforms+docs | 0.4d |
| C'204 | ImportWizard Step1 TAB 解禁 + フィルタ | winforms | 0.2d |
| C'205 | 実 TAB ファイル E2E (手動) — feature_current 4326 確認 | manual | 0.5d |
| C'206 | `TabPackageTests` + `SridCatalogBootstrapperTests` + GdalLayerSourceContractTests TAB サンプル | tests | 0.3d |

## WC'3 — UcsDetect + Required UI + Docs

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| C'301 | NuGet `UtfUnknown` + `UcsDetectResolver` 新規 (信頼度 ≥ 0.7 採用) | winforms | 0.5d |
| C'302 | `MifTabCharSetParser` + UcsDetect と統合 (MIF/TAB CharSet ヘッダ最優先) | winforms | 0.3d |
| C'303 | SchemaGrid Required 列編集可 + `RequiredOverridden` フラグ + ツールチップ | winforms | 0.5d |
| C'304 | `UcsDetectResolverTests` (5 文字コード) + `SchemaGridRequiredOverrideTests` | tests | 0.4d |
| C'305 | `docs/layer-import.md` Phase C' 章 + `PHASE_C_PRIME_COMPLETE.md` + メモリ更新 | docs | 0.3d |

## 起票時のテンプレート

```markdown
## 課題
(Plan の §X.1 をコピー)

## 採用方針
(Plan の §X.2 採用案をコピー)

## 影響範囲
(Plan の §X.3 をコピー)

## 受入条件
- [ ] (Wave Plan の検証項目)
- [ ] テストが green (`-c Release`)

## 関連
- 親 Wave: WC'N (#N)
- Design: docs/XXX.md
```

## マイルストーン

`Phase C': MIF/TAB + ローカル CS + UCSDet + IImportPackage + Required トグル`

## 並列実行の指針

- 各 Wave 内: 同 Wave 内の独立 Issue は同 PR にまとめる
- Wave 間: WC'1 → WC'2 → WC'3 シリアル (依存あり)
- WC'3 内は UcsDetect と Required UI が独立で並列可
