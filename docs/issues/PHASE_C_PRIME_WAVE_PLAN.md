# Phase C' Wave Plan

## クリティカルパス

```
WC'0 (Plan + Design 4 本, 0.5d)
   │
   ▼
WC'1 (IImportPackage 抽象 + MIF/MID, 2.5d)
   │
   ▼
WC'2 (TAB + SridCatalog + 和歌山 E2E, 2.5d)
   │      ┌──────────────┐
   ▼      ▼              ▼
WC'3 (UcsDetect + Required UI + Docs, 2.0d)
```

合計 7.5d、クリティカルパス WC'0 → WC'1 → WC'2 → WC'3 = 7 営業日。WC'3 は 2 系統並列。

## WC'0 — Plan + Design (Gate)

ブランチ: `feature/phase-c-prime-wc0-design`

| Issue | 内容 | 工数 |
|-------|------|------|
| **C'100** | Plan 3 本 + Design 4 本 + Index 作成 | 0.5d |

成果物:
- `docs/PHASE_C_PRIME_INDEX.md`
- `docs/issues/PHASE_C_PRIME_{PLAN,WAVE_PLAN,ISSUES_INDEX}.md`
- `docs/import-package-abstraction.md` (Design)
- `docs/srid-catalog.md` (Design)
- `docs/encoding-ucs-detect.md` (Design)
- `docs/import-wizard-required-toggle.md` (Design)

検証: 全 8 ドキュメント markdown lint pass、リンク切れなし。

## WC'1 — IImportPackage + MIF/MID

ブランチ: `feature/phase-c-prime-wc1-mif`

| Issue | 内容 | 工数 |
|-------|------|------|
| **C'101** | `IImportPackage` インタフェース新規 + `ShapefilePackage` 実装に書き換え (`ShpPath` は後方互換で残置) | 0.5d |
| **C'102** | `MifPackage : IImportPackage` 新規 + `CharSet` ヘッダ抽出 | 0.7d |
| **C'103** | `GdalLayerSource` の `_package.PrimaryPath` 参照に変更 + driver switch 整理 | 0.5d |
| **C'104** | ImportWizard Step1 で MIF/MID 解禁 (FormatItems から "Phase C' 対応予定" 削除 + ファイルフィルタ) | 0.3d |
| **C'105** | `MifPackageTests` + `GdalLayerSourceContractTests` MIF サンプル | 0.5d |

検証:
- MIF/MID 1 件投入 → feature_current INSERT 成功
- 既存 Shapefile テスト全 green
- `dotnet build/test windos-app -c Release` 全 green

並列度: C'101 → (C'102, C'103) 並列 → C'104 → C'105。

## WC'2 — TAB + SridCatalog + 和歌山 E2E

ブランチ: `feature/phase-c-prime-wc2-tab-and-srid`

| Issue | 内容 | 工数 |
|-------|------|------|
| **C'201** | `TabPackage : IImportPackage` 新規 (.tab + .map + .dat + .id + 任意 .ind、CharSet + CoordSys 抽出) | 0.7d |
| **C'202** | `ImportOptions.SridCatalog` プロパティ追加 + `SridCatalogEntry` + `SridCatalogBootstrapper` (起動時 RegisterWkt) | 0.4d |
| **C'203** | 和歌山旧測地系 WKT 本体収録 (`appsettings.json` の `Import:SridCatalog[]` に 2 件) + 出典コメント | 0.4d |
| **C'204** | ImportWizard Step1 で TAB 解禁 + ファイルフィルタ | 0.2d |
| **C'205** | 実 TAB ファイル E2E (DetectShapefile → Step2 → Step3 → feature_current 4326 確認) | 0.5d |
| **C'206** | `TabPackageTests` + `SridCatalogBootstrapperTests` + GdalLayerSourceContractTests TAB サンプル | 0.3d |

検証:
- 和歌山 TAB → feature_current に EPSG:4326 で書き込み (手動 E2E)
- SridCatalog 1 件追加 → 再起動 → 同 SRID TAB が `SridResolutionState.Detected`
- 既存テスト全 green + 新規テスト pass

並列度: C'201 / (C'202 → C'203) 並列、C'204 → C'205 → C'206。

## WC'3 — UcsDetect + Required UI + Docs

ブランチ: `feature/phase-c-prime-wc3-encoding-and-ui`

| Issue | 内容 | 工数 |
|-------|------|------|
| **C'301** | NuGet `UtfUnknown` 追加 + `UcsDetectResolver` 新規 (信頼度 ≥ 0.7 採用、未満 CpgFile フォールバック) | 0.5d |
| **C'302** | `MifTabCharSetParser` 新規 + MIF/TAB CharSet ヘッダ → .NET Encoding 名称への正規化 + UcsDetect と統合 | 0.3d |
| **C'303** | ImportWizard Step2 SchemaGrid Required 列編集可化 + `RequiredOverridden` フラグ + ツールチップ | 0.5d |
| **C'304** | `UcsDetectResolverTests` (CP932 / UTF-8 BOM / UTF-16 LE / EUC-JP / CP949 の 5 サンプル) + `SchemaGridRequiredOverrideTests` | 0.4d |
| **C'305** | `docs/layer-import.md` Phase C' セクション + `PHASE_C_PRIME_COMPLETE.md` + メモリ更新 | 0.3d |

検証:
- CP949 .dbf → UcsDetect 検出 → 文字化けなし Step2 表示
- Step2 Required 手動 ON → ImportAsync 後 schema で `required=true`
- 全テスト pass + ドキュメント lint OK

並列度: C'301 → C'302 // C'303 並列、C'304 後段、C'305 最後。

## 全 PR

| Wave | ブランチ | base |
|------|---------|------|
| WC'0 | `feature/phase-c-prime-wc0-design` | main |
| WC'1 | `feature/phase-c-prime-wc1-mif` | main |
| WC'2 | `feature/phase-c-prime-wc2-tab-and-srid` | main |
| WC'3 | `feature/phase-c-prime-wc3-encoding-and-ui` | main |

すべて `base=main`。マージ順 WC'0 → WC'1 → WC'2 → WC'3 推奨。

## リスク

- **R1 TAB sidecar 確認**: zip 内に複数 .tab → `InvalidDataException`、Shapefile 流儀踏襲 (1 zip 1 dataset)
- **R2 MIF/TAB CharSet 優先順位**: MIF/TAB は CharSet ヘッダ最優先、SHP は UcsDetect → CpgFile → Default
- **R3 `IImportPackage` の段階移行**: `ShapefilePackage.ShpPath` 残置で既存テスト保護、WC'1 末で `PrimaryPath` に統一
- **R4 和歌山 WKT 出典**: EPSG コード不在、proj DB / GSI から引用 + 出典 URL を appsettings コメント、テストは `IsSupported(99001)==true` レベル
- **R5 UcsDetect 誤検出**: 信頼度 0.7 で採用、CI 5 サンプル 100% でないなら閾値上げ
- **R6 Required トグルと 422**: ユーザが Required ON で空値ありデータ投入 → API 422、Step2 に戻る UX を Design で確定 (ツールチップで警告)
