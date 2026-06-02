# レイヤ管理 + インポート (Phase B + C)

Phase B「レイヤ編集 + レイヤインポート」+ Phase C「Shapefile + GDAL インポート」の機能仕様と運用ガイド。

## 全体像

- 管理者 (`admin` role) は **WinForms の LayerAdminForm** から GUI でレイヤを作成・削除・インポート
- 対応形式 (Phase B): **GeoJSON** + **CSV (lat/lng 列)**
- Shapefile / MapInfo MIF / TAB は **Phase C 申し送り** (GDAL 投入とセット)
- WinForms 側で形式パース + SRID 4326 化 → API は常に 4326 GeoJSON を受領
- API は 1 chunk (デフォルト 1000 件) を 1 Tx の `fn_feature_insert × N` で投入 (専用バルク関数なし)
- バイテンポラル原則を守り、audit_log は **1 feature 1 行** (Phase A から継承)

## アーキテクチャ

```
WinForms (LayerAdminForm)              API                    DB
  ├ LayerAdminForm                      MapGroup
  │ ├ DataGridView (一覧)               /api/admin/layers       layers
  │ ├ 新規インポート → ImportWizardForm                          ├ description
  │ │  ├ Step1 SourceFormat+File                                ├ source_format
  │ │  ├ Step2 SchemaGrid 編集 ←→ ILayerSource.InferSchemaAsync ├ source_srid
  │ │  └ Step3 投入実行                                          ├ geometry_type
  │ │     ├ CreateLayer (POST)         POST .../layers           ├ created_by
  │ │     ├ StartImportJob (POST)      POST .../{id}/import-jobs ├ created_org_id
  │ │     ├ chunk × N (POST)           POST .../{id}/features/bulk ─→ fn_feature_insert × N
  │ │     └ Finalize (POST)            POST .../import-jobs/{id}/finalize
  │ │                                                            layer_import_job
  │ └ 削除 → DELETE .../layers/{id} ─→ fn_layer_delete (deleted_at)
```

## 対応形式

| 形式 | Phase B | Phase C | Phase C' (申し送り) |
|------|:------:|:------:|:------:|
| GeoJSON (.geojson, .json) | ◯ `System.Text.Json` 直接 | — | — |
| CSV (lat/lng 列) | ◯ 自作 `CsvLayerSource`, `ProjNet` で 4326 化 | — | — |
| **Shapefile (.shp+.shx+.dbf+.prj+.cpg, zip)** | × | ◯ **GdalLayerSource + MaxRev.Gdal.Minimal SKU** | — |
| MapInfo MIF/MID | × | × | ◯ `MapInfo File` driver (Minimal SKU 含有確認済) |
| MapInfo TAB | × | × | ◯ 同上 |
| KML / KMZ | × | × | (Phase D 候補) `LIBKML` driver も Minimal SKU に含有 |

Phase C WC0 PoC で `MaxRev.Gdal.WindowsRuntime.Minimal` SKU の OGR driver 含有を実機検証 (`docs/issues/PHASE_C_C100_POC_RESULT.md`)。MIF/TAB/KML は全て同 SKU に含まれているため、Phase C' / Phase D の追加実装は **Full SKU 切替不要**。

**Phase C 申し送り (実装リスク回避)**:
- GDAL は `MaxRev.Gdal.Core` + `MaxRev.Gdal.WindowsRuntime.Minimal` の組み合わせ予定
- **`SHAPE_ENCODING` はプロセス環境変数ではなく `Ogr.Open(path, options)` の `ENCODING=CP932` オプションで指定** (xUnit 並列実行と非互換のため)
- MIF/TAB の和歌山測地系などローカル SRID は実機検証 1 人日を別途確保

## スキーマ推論

`Services/Import/InferenceStrategies/` 配下に純粋関数化:

| 形式 | 戦略 | アルゴリズム |
|------|------|---|
| GeoJSON | `GeoJsonInferenceStrategy` | 最初の 100 feature の properties から `JsonValueKind` を直接マップ。Number で `TryGetInt64` 成功なら `integer`、混在で `number`、ISO8601 date 形式は `date`、混在は `string` に丸め |
| CSV | `CsvInferenceStrategy` | 100 行サンプリングで `boolean → date → integer → number → string` の順に試行。空セル混在で `nullable=true`。lon/lat 列は除外 |

UI 側 (`SchemaGrid`) で type / required をユーザが調整可能。

## SRID 変換

`Services/Import/SridConverter.cs` (`ProjNet` 薄ラッパ):

- 既知 SRID キャッシュ: `4326` (WGS84) / `4612` (JGD2000) / `6668` (JGD2011) / `3857` (Web Mercator)
- `(sourceSrid, x, y) → (lon4326, lat4326)`
- 4326 同一は no-op
- 未対応 SRID は `NotSupportedException`

## バルク投入の運用

`appsettings.json`:
```json
{
  "BulkInsert": {
    "MaxCountPerChunk": 5000,
    "ChunkDefaultSize": 1000
  }
}
```

- `MaxCountPerChunk` 超過 → **413 PayloadTooLarge** ProblemDetails
- WinForms 既定 chunkSize = 1000 (WB0 性能スパイクの根拠による)
- 1 chunk = 1 Tx + 1 UUID `p_request_id` (audit_log で関連付け可)

### 性能特性 (WB0 B506 実測 / 5000 件 GeoJSON / Testcontainers PostGIS)

| chunkSize | total elapsed | per feature | max chunk |
|----------:|--------------:|------------:|----------:|
| 500  | 2.02 s | 403 μs | 237 ms |
| **1000** | **1.89 s** | **377 μs** | **380 ms** |
| 2000 | 1.83 s | 367 μs | 743 ms |

`ChunkDefaultSize=1000` を採択 (Phase B)。30s `CommandTimeout` に対し安全マージン十分。10 万件級でも約 40 秒で完了する想定。100 万件級は Phase C で `fn_feature_bulk_insert` 専用関数を検討。

## API endpoint 一覧 (Phase B 新規)

| メソッド | パス | 認可 | 概要 |
|---|---|---|---|
| GET | `/api/admin/layers?includeDeleted=false` | admin | 一覧 |
| POST | `/api/admin/layers` | admin | 作成 (`fn_layer_create`) |
| GET | `/api/admin/layers/{id}` | admin | 単体 |
| PATCH | `/api/admin/layers/{id}` | admin | name/type/geometryType/description 部分更新 |
| DELETE | `/api/admin/layers/{id}` | admin | 論理削除 (`fn_layer_delete`、`running` ジョブ中は **409**) |
| POST | `/api/admin/layers/{id}/import-jobs` | admin | ジョブ開始 (二重で **409**) |
| GET | `/api/admin/layers/import-jobs/{jobId}` | admin | 進捗参照 |
| POST | `/api/admin/layers/import-jobs/{jobId}/finalize` | admin | succeeded/failed 通知 (既 finalize で **409**) |
| POST | `/api/admin/layers/{id}/features/bulk` | admin | 1 chunk 投入 (`Count>Max` で **413**) |

既存 `PUT /api/admin/layers/{layerId}/schema` は残置 (案 C 論点 12)。

## 暫定回避策

Phase C 対応外の形式 (MIF/TAB/KML 等) は、ユーザ側で **QGIS** や **ogr2ogr CLI** で GeoJSON に変換してから取り込んでもらう運用:

```
ogr2ogr -f GeoJSON output.geojson input.tab
ogr2ogr -f GeoJSON output.geojson input.kml
```

## トラブルシュート

- **413 が出る**: チャンクサイズが `MaxCountPerChunk=5000` を超えている。WinForms は 1000 で送るが、CLI 等から大量に投げる場合は分割
- **409 (DELETE)**: 同 layer に `status='running'` のジョブがある。先に `finalize` するか、ジョブ完了待ち
- **CSV で型推論が想定と違う**: ヘッダ 1 行目を確認、100 行以上の場合は Phase B では先頭 100 行のみ走査。Step2 SchemaGrid で手動で型を直す
- **属性スキーマ違反 (422)**: bulk endpoint がチャンク単位で Tx rollback。手前のチャンクは確定済 (`finalize { status:"failed" }` 通知の上、必要なら DELETE で巻き戻し)

---

# Phase C: Shapefile + GDAL インポート

Phase C で追加された Shapefile zip 取り込みの仕様 + 運用。

## 全体像

- WinForms に **MaxRev.Gdal.Core + WindowsRuntime.Minimal SKU** を同梱 (x64 固定)、`Program.Main` 先頭で `GdalBase.ConfigureAll()` 呼び出し
- ImportWizardForm Step1 で **Shapefile ZIP** を選択 → 自動検出 → Step2 でスキーマ調整 → Step3 で投入
- API/DB は **変更ゼロ**: Phase B 確立の `fn_feature_insert` / `audit_log.meta_jsonb` / `BulkInsert` をそのまま利用
- Multi 正規化: Polygon → MultiPolygon、LineString → MultiLineString に昇格 (案 P §6.5)
- Z/M 値: X/Y のみ抽出 + WARN ログ

## アーキテクチャ (Phase C 追加分)

```
WinForms
  ImportWizardForm.Step1 (Shapefile ZIP 選択)
    ├ shapefileOptionsGroup
    │  ├ [自動検出] ボタン
    │  ├ 文字コード ComboBox (.cpg 自動 / CP932 / UTF-8 / EUC-JP)
    │  └ 手動 SRID 入力欄 (FallbackToPrompt 時必須)
    └ shapefileInlinePanel (検出後 inline 表示)
       ├ 検出文字コード
       ├ 検出 SRID
       ├ SridResolutionState (Detected / FallbackToPrompt / FallbackToWgs84 / Rejected)
       └ FieldCount / FeatureCount

ImportWizardViewModel.DetectShapefileAsync(ct)
  ├ ShapefilePackage.OpenAsync (zip → temp dir 実展開)
  ├ ISridDetector.DetectAsync (.prj → OGR SpatialReference → AuthorityCode)
  ├ IEncodingResolver.Resolve (.cpg → CpgFileParser)
  └ GdalLayerSource
     ├ InferSchemaAsync → GdalInferenceStrategy
     └ ReadFeaturesAsync (IAsyncEnumerable<GeoJsonFeature>)

→ Phase B 経路 (CreateLayer / StartImportJob / Bulk × N / Finalize) で API へ
```

## 設定値 (`appsettings.json: Import` 拡張)

```json
{
  "Gdal": { "ConfigureOnStartup": true },
  "Import": {
    "SridFallbackPolicy": "PromptUser",
    "DefaultDbfEncoding": "CP932"
  }
}
```

- `Gdal:ConfigureOnStartup`: `false` で `ConfigureAll()` スキップ (GDAL を使わないテスト等)
- `Import:SridFallbackPolicy` 3 値:
  - `Reject`: `.prj` 不在で Step1 Next 非活性化 (インポート不可)
  - `PromptUser` (デフォルト): UI に手動 SRID 入力欄を表示、ユーザに確定させる
  - `AssumeWgs84`: 4326 で続行、`audit_log.meta_jsonb.srid_inferred=true` を記録
- `Import:DefaultDbfEncoding`: `.cpg` 不在時の fallback (CP932 既定)

## 文字コード解決順 (PHASE_C_DESIGN_P §6.10)

1. UI ComboBox 上書き (空でなければそれを優先)
2. `.cpg` ファイル内容 (`CpgFileParser` で正規化: `"932"→"CP932"`, `"SJIS"→"CP932"`, `"UTF8"→"UTF-8"` 等)
3. `Import:DefaultDbfEncoding` (デフォルト `CP932`)

環境変数 `SHAPE_ENCODING` は **使用しない** (xUnit 並列実行と非互換のため、Design 決定 6)。代わりに、`.cpg` ファイルが zip に含まれていない場合は temp dir に解決済 encoding を書き込んで OGR の自動検出に乗せる。

## ローカル CS 追加 (`SridConverter.RegisterWkt`)

```csharp
// 例: 和歌山旧測地系を動的登録 (Phase C' で外部設定化予定)
sridConverter.RegisterWkt(99999, "GEOGCS[...]");
```

- `Phase C` 本体は API 公開のみ、WKT 本体収録 (和歌山旧測地系等) は **Phase C' 申し送り** (`appsettings.json: Import:SridCatalog[]` 経路)
- 重複登録は後勝ち、不正 WKT は ProjNet 例外を呼び出し側に伝播

## OFT → InferredField 写像 (`GdalInferenceStrategy`)

| OGR FieldType | InferredField.Type | 備考 |
|---------------|--------------------|------|
| `OFTInteger` / `OFTInteger64` | `integer` | |
| `OFTReal` | `number` | |
| `OFTString` | `string` | 100 件全 ISO8601 で `date` 昇格 |
| `OFTDate` / `OFTDateTime` / `OFTTime` | `date` | |
| `OFTBinary` | (skip + WARN) | inferred fields に含まれない |
| `OFTStringList` / `*List` | `string` | JSON 文字列化 |

100 feature サンプリングで 1 件でも null/空文字なら `nullable=true`。

## 配布サイズ (実測)

- ネイティブ DLL: 18 ファイル / 30.8 MB (`gdal*` / `proj*` / `geos*` / `sqlite*` / 各種エンコーダ)
- WinForms ビルド出力 (Release): 約 100 MB

WC0 PoC 値 94.4 MB + Phase B 既存 = 100 MB (受け入れ条件 ±10% 内)。ClickOnce / MSIX 差分配布最適化は **Phase D 申し送り**。

## Phase C' 申し送り

`PHASE_C_DESIGN_P.md` §6.12 参照:

1. **MIF/MID** 対応 (`GdalLayerSource` の driver 名引数 `"MapInfo File"` + `MifPackage`、1.5 人日)
2. **TAB** 対応 (同 driver + `TabPackage`、1.5 人日)
3. **`IImportPackage` 抽象切り出し** (3 形式実装が揃った時点で共通化、1.0 人日)
4. **和歌山旧測地系等ローカル CS WKT 本体収録** (`appsettings.json: Import:SridCatalog[]` 経路、1.5 人日)
5. **`UcsDetectResolver` 実装** (UTF-16 / EUC-JP / CP949 自動検出、0.8 人日)

## Phase D 申し送り

1. **KML / KMZ** 対応 (`LIBKML` driver は Minimal SKU に含有確認済)
2. **GeoPackage / FGB / GPX / DXF** (driver 登録のみで対応可能)
3. **BulkInsertMaxCount 上限解除** + `fn_feature_bulk_insert` 専用関数の必要性判断 (10 万件 E2E 結果次第、audit_log バッチ集約は不採用方針堅持)
4. **サーバ側 GDAL** (GeoServer / 内製タイラ、`scale-target-and-server-side-rendering` メモリと同時設計)
5. **ClickOnce / MSIX 差分配布**

## WebGIS への影響

**Phase C は WebGIS に影響ゼロ**。インポートは PostGIS テーブルへの書き込みに完結し、WebGIS は同じ `feature_current` を読むだけ。Phase D で WebGIS が GeoServer 経由のタイル表示に切り替わっても、Phase C のインポート経路は無変更で機能する。

## Phase C 追加 トラブルシュート

- **「Shapefile を選択しても Next が非活性」**: 自動検出ボタンを押していないか、`SridResolutionState=Rejected/FallbackToPrompt` で手動 SRID 未入力。`appsettings.json: Import:SridFallbackPolicy` を `AssumeWgs84` にすれば 4326 黙認で通せる (audit_log に `srid_inferred=true` 記録)
- **文字化け**: `.cpg` 不在で `DefaultDbfEncoding` 違いの可能性。Shapefile オプションの文字コード ComboBox で上書きして再検出
- **配布サイズが大きい**: GDAL ネイティブ DLL 30 MB 強は不可避。ClickOnce/MSIX 差分配布は Phase D で最適化
- **`SridFallbackPolicy=Reject` で通らない**: 多くの SHP データソースは `.prj` を含まないため、PromptUser に変えるのが現実的
- **テストで OGR を初期化したい**: `[Collection("Gdal")]` を付与し、`GdalFixture` (windos-app.tests) を共有 — `GdalBase.ConfigureAll()` を 1 回だけ呼ぶ並列耐性パターン
