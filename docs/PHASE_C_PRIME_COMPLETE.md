# Phase C' 完了サマリ

Phase C' (MIF/TAB + ローカル CS + UCSDet + IImportPackage + Required トグル) 完了時点の高位サマリ。

## マージ済 PR (全 4 件)

| Wave | PR | 内容 |
|------|----|------|
| WC'0 | [#233](https://github.com/KenjiMiyakoshi/agri-gis/pull/233) | Plan + Design 4 本 (`import-package-abstraction.md`, `srid-catalog.md`, `encoding-ucs-detect.md`, `import-wizard-required-toggle.md`) |
| WC'1 | [#234](https://github.com/KenjiMiyakoshi/agri-gis/pull/234) | C'101 IImportPackage 抽象 + ShapefilePackage 実装 + IEncodingResolver/ISridDetector シグネチャ変更 + C'102 MifPackage + C'103 GdalLayerSource IImportPackage 化 + C'104 Step1 MIF 解禁 + C'105 MifPackageTests |
| WC'2 | [#235](https://github.com/KenjiMiyakoshi/agri-gis/pull/235) | C'201 TabPackage + C'202 SridCatalogBootstrapper + C'203 旧日本測地系 II/IV WKT 収録 + C'204 Step1 TAB 解禁 + C'206 Tests |
| WC'3 | 本 PR | C'301 UcsDetectResolver + UTF.Unknown NuGet + C'302 MifTabCharSetParser + C'304 Tests + C'305 Docs |

## 受入条件

1. ✅ MIF/MID 1 件 (CP932) インポート → 動作確認可能 (WC'1)
2. ✅ TAB ZIP (ローカル CS) インポート → SridCatalog で SRID 解決可能 (WC'2)
3. ⏸ CP949 (Hangul) .dbf → UcsDetect 検出 (実 zip テストは Phase C'' のテストインフラ整備時)
4. ⏸ Step2 Required トグル UI → **Phase C'' 送り** (sampleCoversAll 推論の override は SchemaGrid 構造変更が広範になるため別 PR で精緻化)
5. ✅ `dotnet test windos-app.tests -c Release` **148 / 148 pass** (既存 125 + Phase C' WC'1 4 + WC'2 6 + WC'3 13)
6. ✅ `dotnet test api.tests -c Release` 89 件 keep (Phase E' 完了時点から regression なし)
7. ✅ 全 4 Wave が main にマージ済 (#233-#235 + 本 PR)

## 主要な実装メモ

- **`IImportPackage` 最小 API**: `PrimaryPath` + `MissingOptional` + `IAsyncDisposable` のみ。`ShapefilePackage.ShpPath` は後方互換で残置 (段階移行)
- **MIF/TAB ヘッダ抽出**: 先頭 50 行を UTF-8 で読み、`CharSet "..."` と `CoordSys ...` を抽出 (`Data` 行で MIF ヘッダ終端)
- **SridCatalog**: `appsettings.json` の `Import:SridCatalog[]` で起動時 RegisterWkt 一括、99001/99004 (旧日本測地系 II/IV、和歌山・三重) を初期収録
- **UcsDetect**: `UTF.Unknown` NuGet (Mozilla Universal Chardet .NET port、MIT)、信頼度 ≥ 0.7 採用、未満は CpgFile フォールバック
- **MifTabCharSetParser**: MIF/TAB CharSet ヘッダ ("WindowsJapanese" 等) を OGR/.NET Encoding 名 ("CP932" 等) に正規化する dictionary
- **`Encoding.GetEncoding` の environment 依存**: `CodePagesEncodingProvider` 登録なしで CP932 等が取れない → `ToEncoding` メソッドは public API から外し、利用側で `Encoding.GetEncoding(ToEncodingName(x))` パターンに

## Phase C'' 申し送り

- **ImportWizard Required トグル UI** (`docs/import-wizard-required-toggle.md` Design あり、SchemaGrid + ViewModel 改修)
- **UcsDetectResolver の実 zip テスト** (テストインフラ整備、ShapefilePackage の internal ctor 開放 or test factory)
- **和歌山実 TAB E2E** (CI 非配置、検証手順は本 docs 参照)
- **KML / KMZ / DXF / DGN CAD 形式** (LIBKML 等の Minimal SKU 含有確認 PoC)
- **GeoPackage 出力**
- **TAB フォルダ選択経路** (現状 zip 必須)
- **UcsDetect 信頼度しきい値の admin 画面設定化** (現状 appsettings)
- **`SridCatalog` の DB 永続化** (組織ごとに違う SRID 集合)
- **MifTabCharSetParser を ImportWizardViewModel.CreateSourceAsync の MIF/TAB 経路に統合** (現状は ImportWizardViewModel が CharSet ヘッダを使わずに resolver 任せ。Phase C'' で `MifPackage.CharSetHeader` → `MifTabCharSetParser.ToEncodingName` → `InlineEncodingResolver` の優先経路を組む)

## 実 TAB ファイル動作確認手順 (本番運用前)

1. 和歌山旧測地系の TAB ZIP を用意 (例: 和歌山県農業試験場の圃場図)
2. `appsettings.json` の `SridCatalog` を実 SRID (99004) と本データの CoordSys に合致する WKT で更新
3. WinForms 起動 → 管理メニュー → レイヤ管理 → 新規インポート
4. Step1: 形式 = "MapInfo TAB ZIP"、ファイル選択
5. Step1: [自動検出] (Phase C' WC'3 では Shapefile のみ実装、TAB は手動 SRID 入力で代替) — SRID 99004 を ManualSridInput で指定
6. Step2: フィールド一覧を確認
7. Step3: 投入実行 → 完了後 PostGIS で `SELECT ST_AsGeoJSON(geom) FROM feature_current WHERE layer_id = N LIMIT 1` で 4326 座標確認

## 関連

- `PHASE_C_PRIME_INDEX.md` (着手時計画)
- `docs/import-package-abstraction.md`, `srid-catalog.md`, `encoding-ucs-detect.md`, `import-wizard-required-toggle.md`
