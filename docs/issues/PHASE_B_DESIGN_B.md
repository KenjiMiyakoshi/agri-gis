# Phase B Design — 案 B: 段階導入 (GeoJSON+CSV 先行 / GDAL 不投入)

Plan (PHASE_B_PLAN.md) で挙げた 5 形式・GDAL 同梱・バルク API 一括導入の構成に対し、本案は **Phase B では GeoJSON / CSV だけを完全対応**、Shapefile / MapInfo MIF / MapInfo TAB は **Phase C で GDAL を投入してから追加** する段階導入戦略を取る。早く LayerAdminForm を Production に乗せ、認可マトリクスとバイテンポラルとの結合を実機で検証することを最優先する。

## 0. 全体方針

- **薄く速く**: API は CRUD 4 本 + バルク 1 本のみ。バルクは新 PL/pgSQL を作らず `fn_feature_insert` を Tx ループで使い回す
- **C# pure**: WinForms に GDAL を入れない。GeoJSON は `NetTopologySuite.IO.GeoJSON4STJ`、CSV は `System.IO`/手書きパーサで足りる
- **Phase C 受け渡し点を明示**: SHP/MIF/TAB のためのフック (LayerImporter インタフェース / `source_format` 列) は B で先に切っておき、Phase C は実装追加だけで済むようにする
- **Review② 負債は H2 のみ合流**: B4/B5 で新規 endpoint を増やす機会に JsonOpts を単一化。H4/H5 は触らない (LayerAdminForm を MainForm から独立 Modal で開く構成にしてご当地的に肥大を避ける)

## 1. 対応形式と取り込み経路

| 形式 | Phase B | Phase C |
|---|---|---|
| GeoJSON (.geojson, .json) | ◯ NetTopologySuite で `FeatureCollection` を直接パース | — |
| CSV (Point 専用, lat/lng) | ◯ ヘッダ解析 → POINT(lng lat) 合成 | WKT 列サポート追加 |
| Shapefile (.zip) | × | ◯ GDAL `OGR_Shp` ドライバ |
| MapInfo MIF/MID | × | ◯ GDAL `MapInfo File` ドライバ |
| MapInfo TAB | × | ◯ GDAL `MITAB` ドライバ |

LayerAdminForm の Step1 で形式選択 ComboBox は 5 形式すべて表示するが、Phase B では SHP/MIF/TAB を選んだ瞬間に「Phase C で対応予定」のラベルを出して進行不可とする (UI 文言は実装済みフラグで切替可能)。

## 2. WBS 対応表 (Plan の B1〜B12 を本案にマップ)

| Plan ID | 本案での扱い | 補足 |
|---|---|---|
| B1 layers 拡張 | ◯ Phase B | `description`, `srid_source`, `created_by`, `deleted_at` に加え **`source_format` text** を足す (Phase C で値域拡張) |
| B2 fn_layer_create/delete | ◯ Phase B | 既存 `fn_layer_schema_upsert` と同じく `p_user_id UUID, p_org_id INT` 末尾 |
| B3 バルク insert 関数 | △ Phase B では作らず Phase C | Phase B は `fn_feature_insert` ループで十分。1 万件超は Phase C で要件化 |
| B4 admin layers CRUD | ◯ Phase B | AdminOrgsEndpoints コピー + H2 (JsonOpts) 統合 |
| B5 features:bulk | ◯ Phase B (薄い実装) | GeoJSON FeatureCollection 受信 → ループ。1000 件で 1 Tx |
| B6 LayerAdminForm 骨格 | ◯ Phase B | MainForm からは `Show()` で独立 Modal、メニュー周りリファクタは Phase C |
| B7 取り込み Step1 | ◯ Phase B (GeoJSON/CSV のみ) | OGR DataSource 抽象は `ILayerSource` インタフェースで切る → Phase C で GdalLayerSource を追加 |
| B8 スキーマ推論 + 編集 | ◯ Phase B | GeoJSON properties / CSV ヘッダから推論 |
| B9 SRID + 投入 | ◯ Phase B (簡略) | GeoJSON は仕様上 4326 固定とみなす。CSV はユーザに SRID 指定させて API 受信時 `ST_Transform` |
| B10 認可マトリクステスト | ◯ Phase B | admin/general/guest × CRUD + bulk |
| B11 スキーマ推論単体テスト | ◯ Phase B (GeoJSON/CSV 分のみ) | Phase C で SHP/MIF/TAB fixture 追加 |
| B12 Docs | ◯ Phase B | `docs/layer-import.md` に「Phase B 範囲」「Phase C 予定」を明記 |

合計目安: Plan の 14〜18 人日から **約 9〜12 人日に短縮** (B3 と B7/B11 の SHP・MIF・TAB 分が後ろ倒し)。Phase C 追加見積: 約 5〜7 人日。

## 3. アーキテクチャ

### 3.1 WinForms 側

```
LayerAdminForm
 ├─ LayerListGrid     (DataGridView, GET /api/admin/layers)
 ├─ ToolStrip         [新規] [編集] [削除]
 └─ ImportWizardDialog (Modal)
      ├─ Step1: SourceFormatPicker → ILayerSource を生成
      ├─ Step2: SchemaInferencePanel (DataGridView)
      └─ Step3: SridConfirmPanel + ProgressDialog
```

`ILayerSource` の Phase B 実装:

```csharp
interface ILayerSource : IDisposable {
    string SourceFormat { get; }              // "geojson" | "csv"
    int? SourceSrid { get; }                  // GeoJSON=4326, CSV=null (要ユーザ指定)
    IReadOnlyList<InferredField> InferSchema(); // 100 行サンプリング
    IAsyncEnumerable<GeoJsonFeature> ReadFeaturesAsync(CancellationToken ct);
}

// Phase B
class GeoJsonLayerSource : ILayerSource { /* NetTopologySuite.IO */ }
class CsvLayerSource     : ILayerSource { /* lat/lng → POINT */ }
// Phase C で追加
// class GdalLayerSource : ILayerSource { /* OGR DataSource をラップ */ }
```

`ApiClient` の追加メソッド:

```csharp
Task<LayerDto>  CreateLayerAsync(CreateLayerRequest req, CancellationToken ct);
Task<LayerDto>  UpdateLayerAsync(int id, UpdateLayerRequest req, CancellationToken ct);
Task            DeleteLayerAsync(int id, CancellationToken ct);
Task<BulkResult> BulkInsertFeaturesAsync(int layerId, string geojsonFC, int sourceSrid, CancellationToken ct);
```

### 3.2 API 側

新ファイル `api/Endpoints/AdminLayersEndpoints.cs` (AdminOrgsEndpoints のコピー)。

- `POST   /api/admin/layers` — `{name, layer_type, schema_json, source_format, source_srid}` を受け `fn_layer_create` 呼び出し
- `GET    /api/admin/layers` — 既存 `GET /api/layers` と違い `deleted_at IS NOT NULL` も含める
- `PATCH  /api/admin/layers/{id}` — name / description / schema_json をまとめて受ける (既存 `PUT /api/admin/layers/{id}/schema` は **Phase B は残置**、Phase C で deprecate)
- `DELETE /api/admin/layers/{id}` — `fn_layer_delete` で論理削除 (`layers.deleted_at` セット)。feature は触らない (バイテンポラル原則)
- `POST   /api/admin/layers/{id}/features:bulk` — GeoJSON FeatureCollection + `source_srid` を受け 1000 件 / Tx でループ

すべて `RequireRole("admin")`。`ICurrentUser` から `p_user_id` / `p_org_id` を取り出して PL/pgSQL に渡す。

### 3.3 DB 側

- `db/migration/B01_layers_extension.sql`: 既存 `layers` に列追加
  - `description text NULL`
  - `source_format text NULL CHECK (source_format IN ('geojson','csv','shapefile','mif','tab') OR source_format IS NULL)` — Phase B は `geojson|csv` のみ運用上許可、CHECK は将来分も含める
  - `source_srid int NULL`
  - `created_by uuid NULL REFERENCES users(id)`
  - `deleted_at timestamptz NULL`
- `db/migration/B02_fn_layer_create_delete.sql`: `fn_layer_create(p_name, p_layer_type, p_schema_json, p_source_format, p_source_srid, p_user_id, p_org_id)` / `fn_layer_delete(p_layer_id, p_user_id, p_org_id)`
- バルク投入は **DB 関数を増やさず**、API 側で `BEGIN; fn_feature_insert(...) ×N; COMMIT;` を回す

## 4. 設計論点 15 件への回答 (Phase B / Phase C)

| # | 論点 | Phase B での回答 | Phase C で足す |
|---|---|---|---|
| 1 | バルク insert 戦略 | (a) `fn_feature_insert` を API Tx ループ。1000 件で chunk commit | (b) `fn_feature_bulk_insert(JSONB[])` を追加、SHP 1 万件超向け |
| 2 | SRID 変換責務 | GeoJSON: 4326 固定で送り API は `ST_Transform(geom,3857)` / CSV: ユーザ指定 SRID を `ST_Transform` | GDAL 取り込み時は WinForms 側で 4326 化して送る (`OGRCoordinateTransformation`) |
| 3 | 型推論優先順位 | GeoJSON は `JsonValueKind` 直接マップ (Number→number, String→string, True/False→boolean, null は nullable=true) / CSV は 100 行サンプリングで `integer→number→boolean→date→string` | OGR `OFTInteger/OFTInteger64/OFTReal/OFTString/OFTDate/OFTDateTime` → 同マッピング |
| 4 | .dbf 文字コード | **対象外** (SHP 非対応) | (b) `.cpg` 優先 + 無ければ UTF-8 試行失敗で CP932 |
| 5 | layer 論理削除と feature | (a) `layers.deleted_at` だけ立て feature_current はそのまま。`GET /api/features?layer_id=` 側で `JOIN layers WHERE deleted_at IS NULL` を強制 | 変更なし (Phase B の設計を踏襲) |
| 6 | layer_type の意味 | 現行どおり単一文字列。GeoJSON で `MultiPolygon` 混在時は `MultiPolygon` に正規化。`feature_current.geom` は `geometry(Geometry,3857)` のままで型混在を許容 | 必要なら `geometry_type` 列を追加し PostGIS 制約を動的化 |
| 7 | SRID 列の持ち方 | `layers.source_srid` に記録だけ。feature には保持しない | 変更なし |
| 8 | "fields" 命名衝突 | `SchemaFieldDto` をそのまま再利用。`nullable` は `required=false` で表現。`sample_values` は WinForms 内部のみ保持 | `default` フィールドを追加検討 |
| 9 | 大ファイル境界 | (a) WinForms 側で 1000 件 chunk、逐次 `POST /features:bulk` | (b) multipart ストリーム or (c) COPY 経路を必要性に応じて追加 |
| 10 | 進捗とロールバック | (b) レイヤは先に作成 commit、feature 投入失敗時はそこまでの commit 済 chunk を残し、ユーザに「途中まで N 件投入済。続行 or レイヤ削除」を提示 | (c) 再開可能なジョブ ID 方式を追加 |
| 11 | LayerAdminForm 起動権限 | (a)+(c) MainForm 起動時に admin で無ければメニュー非表示 **かつ** サーバ側 `RequireRole("admin")` で 2 重防御。401 は BearerHandler が捕捉 | 変更なし |
| 12 | 既存 `PUT /api/admin/layers/{id}/schema` | **残置**。新 PATCH は name/description/schema_json をまとめて受けるが、schema 単独更新の互換性は維持 | deprecate ヘッダ付与 → 次次フェーズで削除 |
| 13 | MapInfo TAB 取り扱い | **対象外** | zip 必須 (.tab/.dat/.map/.id/.ind 全部含む) + GDAL MITAB 安定性テスト |
| 14 | CSV の geom 列指定 UI | (a)+(b) ヘッダから `lat/latitude/y` / `lng/longitude/x` を自動推測しつつ Step1 で必ずユーザに確定させる ComboBox を出す | WKT 列サポート追加 |
| 15 | テスト fixture 配置 | `windos-app.tests/Fixtures/import/` に GeoJSON / CSV を平文配置。git LFS 不要 | SHP zip / MIF/MID / TAB を同ディレクトリに追加、サイズ次第で LFS |

## 5. Review② 負債の合流

- **H2 (JsonSerializerOptions 重複)**: ◯ Phase B で対応。B4 で `api/Json/JsonOpts.cs` (仮) に集約 → `AdminLayersEndpoints` / 既存 `FeatureEndpoints` / `AdminEndpoints` から参照差し替え
- **H4 (AttributeEditorControl.ParentForm キャスト)**: × Phase B 持ち越し。LayerAdminForm のスキーマ編集 UI は新規 DataGridView で独立し、AttributeEditorControl は触らない
- **H5 (MainForm god class)**: × Phase B 持ち越し。LayerAdminForm は MainForm から独立 Modal で開く構成にして god class 化を加速しない (メニュー項目 1 個 + ハンドラ 1 個追加のみ)。本格分割は Phase C 以降

## 6. リスクと緩和

| リスク | 緩和策 |
|---|---|
| Phase B 出荷後に「SHP が無いと使えない」と現場拒否 | LayerAdminForm Step1 で SHP/MIF/TAB を「Phase C 対応予定」と明示。GeoJSON 変換ツール (QGIS) のリンクを Docs に記載 |
| GeoJSON で 1 万件超を送られて API がタイムアウト | `POST /features:bulk` の上限を 5000 件に制限し、超過時は 413 を返す。WinForms 側で自動 chunk |
| CSV の SRID 指定ミスで feature が地球の裏側に出る | Step3 で投入直前に最初の 5 件の lat/lng と SRID を確認ダイアログに出す |
| Phase C で GDAL 追加時に `ILayerSource` インタフェースが合わない | Phase B 終了前に GdalLayerSource のスタブだけ書いて API シグネチャを検証 |

## 7. Phase C 申し送り (受け渡し点)

Phase B 完了時点で以下を残す:

1. `ILayerSource` インタフェース (Phase C は実装追加のみ)
2. `layers.source_format` CHECK 制約に `shapefile/mif/tab` を予約済み
3. `LayerAdminForm` Step1 の ComboBox に 5 形式すべて表示済 (フラグで有効化)
4. `windos-app/MaxRevGdal.cs` (仮) の TODO コメント — NuGet 追加場所明示
5. `docs/layer-import.md` の「Phase C で追加予定」セクション
6. fn_feature_bulk_insert を作らないまま残す → Phase C で性能要件が出てから実装

## 8. 次ステップ

- Design A (一括導入案) との比較表を `PHASE_B_DESIGN_COMPARISON.md` で作成
- Review エージェント 3 体に投入し、案 P/Q/R へ収束
- B1/B2 の SQL ドラフトと B4 の C# スケルトンを Design 段階で先行起こす (Plan の高リスク項目 B7/B9 は本案では Phase C に逃がしたので Design リスクは低下)
