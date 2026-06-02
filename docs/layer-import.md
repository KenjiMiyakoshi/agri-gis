# レイヤ管理 + インポート (Phase B)

Phase B「レイヤ編集 + レイヤインポート」の機能仕様と運用ガイド。

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

## 対応形式と Phase C 申し送り

| 形式 | Phase B | Phase C |
|------|:------:|:------:|
| GeoJSON (.geojson, .json) | ◯ NetTopologySuite なし、`System.Text.Json` 直接 | — |
| CSV (lat/lng 列) | ◯ 自作 `CsvLayerSource`, `ProjNet` で 4326 化 | — |
| Shapefile (.shp+.shx+.dbf+.prj, zip) | × | ◯ GDAL/OGR ドライバ |
| MapInfo MIF/MID | × | ◯ MITAB ドライバ |
| MapInfo TAB | × | ◯ MITAB ドライバ |

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

Phase B 対応外の形式は、ユーザ側で **QGIS** や **ogr2ogr CLI** で GeoJSON に変換してから取り込んでもらう運用:

```
ogr2ogr -f GeoJSON output.geojson input.shp
ogr2ogr -f GeoJSON output.geojson input.tab
```

## トラブルシュート

- **413 が出る**: チャンクサイズが `MaxCountPerChunk=5000` を超えている。WinForms は 1000 で送るが、CLI 等から大量に投げる場合は分割
- **409 (DELETE)**: 同 layer に `status='running'` のジョブがある。先に `finalize` するか、ジョブ完了待ち
- **CSV で型推論が想定と違う**: ヘッダ 1 行目を確認、100 行以上の場合は Phase B では先頭 100 行のみ走査。Step2 SchemaGrid で手動で型を直す
- **属性スキーマ違反 (422)**: bulk endpoint がチャンク単位で Tx rollback。手前のチャンクは確定済 (`finalize { status:"failed" }` 通知の上、必要なら DELETE で巻き戻し)
