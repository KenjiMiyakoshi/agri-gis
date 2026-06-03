# Phase C' Index — MIF/TAB + ローカル CS + UCSDet + IImportPackage + Required トグル

agri-gis Phase C' (`MIF/TAB 対応 + 和歌山旧測地系 + UCSDet 自動検出 + IImportPackage 抽象 + Required トグル UI`) サイクルの高位サマリ。Phase D' / E' 完了後の次サイクル。

## スコープ

Phase C 完了時に `PHASE_C_DESIGN_P.md §6.12` と `orchestration_state.md` で明示した申し送り 6 件を一気に消化する。新規機能は限定的で、**Phase C で公開済の API (`SridConverter.RegisterWkt`, `IEncodingResolver`, `GdalLayerSource` の driver switch) を実装本体で埋めるサイクル**。

## 採用方針

| 観点 | 採用 |
|------|------|
| MIF/MID 対応 | `MifPackage : IImportPackage`、driver `"MapInfo File"`、`CharSet` ヘッダ抽出 |
| TAB 対応 | `TabPackage : IImportPackage` (.tab + .map + .dat + .id + 任意 .ind)、`CharSet` + CoordSys 行抽出 |
| `IImportPackage` 抽象 | WC'1 で MIF 実装と同時に切り出し (実装が机上空論にならないよう最小 API のみ宣言) |
| ローカル CS WKT | `Import:SridCatalog[]` 経路、和歌山旧測地系系 II / IV など 2 件初期収録 |
| UcsDetect | NuGet `UtfUnknown`、信頼度 ≥ 0.7 で採用、未満は CpgFileResolver にフォールバック |
| MIF/TAB CharSet | ヘッダ最優先、SHP は UcsDetect → CpgFile → Default の順 |
| Required トグル | Step2 SchemaGrid の Required 列を編集可に + `RequiredOverridden` フラグ |
| 配布物 | Minimal SKU に MIF/TAB driver 含有確認済 (Phase C WC0 PoC)、追加 DLL なし |
| TAB の入力 | Shapefile と同じく zip 配布前提 (フォルダ選択は Phase C'' 送り) |
| 和歌山実 TAB E2E | 手動検証のみ (CI に置かない、サイズ + 著作権懸念) |

詳細は `docs/issues/PHASE_C_PRIME_PLAN.md`。

## Wave 構成

| Wave | テーマ | 工数 | Issue |
|------|--------|------|------|
| **WC'0** | Plan + Design 4 本 | 0.5d | C'100 |
| **WC'1** | IImportPackage 抽象 + MIF/MID 対応 | 2.5d | C'101-C'105 |
| **WC'2** | TAB 対応 + SridCatalog + 和歌山 E2E | 2.5d | C'201-C'206 |
| **WC'3** | UcsDetect + Required UI + Docs | 2.0d | C'301-C'305 |
| | **合計** | **約 7.5d** | **16 Issue** |

クリティカルパス約 7 営業日 + バッファ 0.5d。WC'3 内は 2 系統並列で短縮可。Phase E' (6.0d) よりやや大きい (TAB E2E + 和歌山 WKT 出典確認の手間込み)。

詳細は `docs/issues/PHASE_C_PRIME_WAVE_PLAN.md`。

## 主要追加

### 新規ファイル

- `windos-app/Services/Import/Packages/IImportPackage.cs` (抽象)
- `windos-app/Services/Import/Packages/MifPackage.cs`
- `windos-app/Services/Import/Packages/TabPackage.cs`
- `windos-app/Services/Import/Encoding/UcsDetectResolver.cs`
- `windos-app/Services/Import/Encoding/MifTabCharSetParser.cs`
- `windos-app/Services/Import/Srid/SridCatalogBootstrapper.cs`
- `appsettings.json` の `Import:SridCatalog[]` (旧日本測地系系 II + IV)

### 変更ファイル

- `ShapefilePackage.cs` (`IImportPackage` 実装、`ShpPath` は後方互換で残置)
- `GdalLayerSource.cs` (driver switch + `_package.PrimaryPath` 参照に変更)
- `ImportWizardForm.cs` + `ViewModel.cs` (Step1 MIF/TAB 解禁 + Step2 Required トグル UI)
- `SchemaGrid.cs` (Required 列編集可)
- `ImportOptions.cs` (`SridCatalog[]` プロパティ追加)

## 受け入れ条件 (Phase C' 完了の定義)

1. ✅ MIF/MID 1 件 (CP932) インポート → feature_current に EPSG:4326 で投入
2. ✅ TAB 1 件 (ローカル CS) インポート → SridCatalog で SRID 解決 → feature_current に EPSG:4326 で投入
3. ✅ CP949 (Hangul) .dbf → UcsDetect で正しく検出 → 文字化けなし Step2 表示
4. ✅ Step2 で Required を手動 ON → ImportAsync 後の schema で `required=true`
5. ✅ `dotnet test windos-app.tests -c Release` 全 green (Phase E' 125 → 推定 140+)
6. ✅ `dotnet test api.tests -c Release` 全 green (89 件 keep)
7. ✅ `docs/layer-import.md` の Phase C' セクション追記
8. ✅ 全 4 Wave が main にマージ済
9. ✅ `orchestration_state.md` メモリ更新

## Phase C'' 申し送り

- **KML / KMZ インポート** (LIBKML driver 既に Minimal SKU 含有)
- **DXF / DGN 等 CAD 形式** (driver 確認 PoC が必要)
- **GeoPackage 出力**
- **TAB のフォルダ選択経路** (現状 zip 必須)
- **UcsDetect の信頼度しきい値の admin 画面設定化** (現状 appsettings のみ)
- **Required トグルの「全フィールド一括 ON/OFF」UI** (現状 1 行ずつ)
- **`SridCatalog` を DB 永続化** (現状 appsettings のみ、組織ごとに違う SRID 集合の運用)

## Phase H 送り

- 本番 GeoServer / helm / MapProxy (Phase D' から keep)

## 関連ドキュメント

- `PHASE_A_INDEX.md` 〜 `PHASE_E_INDEX.md`
- `PHASE_D_PRIME_INDEX.md` + `PHASE_D_PRIME_COMPLETE.md`
- `PHASE_E_PRIME_INDEX.md` + `PHASE_E_PRIME_COMPLETE.md`
- `docs/issues/PHASE_C_PRIME_PLAN.md`
- `docs/issues/PHASE_C_PRIME_WAVE_PLAN.md`
- `docs/issues/PHASE_C_PRIME_ISSUES_INDEX.md`
- `docs/import-package-abstraction.md`
- `docs/srid-catalog.md`
- `docs/encoding-ucs-detect.md`
- `docs/import-wizard-required-toggle.md`

## 関連メモリ

- `orchestration_state.md` — 進捗
- `import_wizard_required_toggle.md` — Required 編集 UI の発端
- `stacked_pr_pitfall.md` — base=main 固定
- `smart_app_control_pitfall.md` — WinForms Release 構成
