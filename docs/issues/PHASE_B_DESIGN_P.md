# Phase B Design 案 P — 採択案 (案 B ベース + 案 A の DB 投資部分マージ + 3 レビュー反映)

Phase B Design A/B/C の 3 案と、拡張性 / 実装リスク / テスタビリティの 3 レビューを統合した採択案。Issue 化フェーズの直接入力となる。

## 1. 採用ベースと選択理由

**ベース案 = 案 B (段階導入 / GeoJSON+CSV 先行 / GDAL 不投入)**

選択理由:
- **3 レビュー横断で最高評価**: 拡張性は `ILayerSource` 抽象が「後から導入が構造的に困難な拡張点」として Phase B 中に切れる点で他案より優位。実装リスクは GDAL/TAB/MIF/.dbf の地雷を Phase C へ逃がせる構造で B-地雷 1/2/3 と A-地雷 1/3 の大半を回避。テスタビリティは Phase B スコープから GDAL を外せたため windos-app.tests が pure C# 化し、CI 単一ランナーで完結する
- **MEMORY.md 整合**: バイテンポラル原則 (1 feature 1 audit) を案 A のバッチ集約より素直に維持できる
- **WebGIS ハイブリッド前提との整合**: GeoJSON を C# pure に扱う実装は将来ブラウザ直送経路 (WebGIS → API) への発展余地を最大化する

## 2. 案 A から取り込む部分 (DB 投資の前倒し)

「Phase B でやらないと後で破壊的になるもの」だけを案 A から先食いする。拡張性レビューの **補強 1** に対応。

| 取り込み項目 | 案 A 由来 | 案 P での扱い |
|---|---|---|
| `layers.created_org_id INT NOT NULL` | B101 | マルチテナント境界を DB に確定。後付け RLS / org_id 強制 WHERE のために必須 |
| `layers.geometry_type TEXT` (`layer_type` と分離) | B101 + 論点 6 | Phase B 投入 feature が少ないうちに分離。あとから既存 feature を再整形するコストを避ける |
| `layer_import_job` テーブル新設 | B105 | **同期完結を変えずに 1 行だけ書く運用**。非同期化 / 進捗 API / 夜間バッチへの発展時に既存データが連続する |
| 共通 `SchemaGrid` UserControl 切り出し (H4 解消) | B303 | Review② 負債合流。`ImportWizardForm.Step2` と `AttributeEditorControl` で共用 |
| `JsonOpts.cs` 集約 (H2 解消) | B201 | Review② 負債合流。新 endpoint 追加機会で潰す |

## 3. 案 C から取り込む部分

| 取り込み項目 | 案 C 由来 | 案 P での扱い |
|---|---|---|
| 既存 `PUT /api/admin/layers/{id}/schema` は **残置**、`PATCH` と棲み分け | 論点 12 | A506 既存テストを無傷で維持。Phase A 完成済 API を変えない |
| fixture を `windos-app.tests/Fixtures/import/` に 1 箇所集約 | 論点 15 | API テストも同所から参照 (`TestPaths` ヘルパ経由) |
| `MainForm.OnLoad` で role による Visible 制御 (`ApplyGuestRestriction` 同型) | 論点 11 | クライアント実装が薄い + Phase A パターンと一致 |
| MainForm メニュー項目は 1 行追加に留め god class を加速しない | 論点 11 / コンセプト | H5 は Phase B では触らない方針を堅持 |

## 4. 却下する部分 (理由付き)

| 却下項目 | 出典 | 理由 |
|---|---|---|
| `fn_feature_bulk_insert(JSONB)` 専用関数 + audit_log バッチ 1 行集約 | 案 A B104 | **バイテンポラル原則違反** (実装リスクレビュー A-地雷 2、MEMORY.md `bitemporal_audit.md` 抵触)。Design Review で差し戻し必至 |
| 5 形式同時対応 (Shapefile/MIF/TAB を Phase B で出す) | 案 A コンセプト | A-地雷 1 (TAB 和歌山測地系の MITAB ドライバ問題が 18 人日に未計上)。Phase C へ逃がす |
| GDAL を WinForms に同梱 (Phase B 時点で) | 案 A / 案 C | テスタビリティ最大の障害 (T-A3 / T-C1)。CI ジョブ物理分割を回避できる |
| `SHAPE_ENCODING` を `Environment.SetEnvironmentVariable` でプロセスグローバル設定 | 案 C 論点 4 | C-地雷 1 (xUnit 並列実行と非互換 / 本番でランダム文字化け)。Phase C で実装する時は **DataSource Open オプション** (`gdal:open_options=ENCODING=CP932`) を使う方針を申し送りに明記 |
| CSV の SRID を API 側 `ST_Transform` で吸収 | 案 B 論点 2 | B-地雷 3 (Phase C で WinForms 側に寄せ直す破壊変更が必要) + テスト B-T2 (Testcontainers 必須化)。**`ProjNet` で WinForms 側 4326 化** に変更 |
| 「1 万件超は Phase B スコープ外」と Docs に明記して諦め | 案 C 論点 1 | B-T3 (上限解除時にテストが意味喪失)。**設定値ベース** (`appsettings.json: BulkInsertMaxCount`) でパラメタ化し境界テストを 2 ケースに集約 |
| H5 (MainForm god class) の Phase B 内修復 | 案 A B301 | スコープ拡大。Phase A 負債は別サイクルで扱う方針を堅持 (案 B/C と同じ判断) |

## 5. 3 レビューの指摘への対応

### 5.1 拡張性レビュー

| 指摘 | 対応 |
|---|---|
| 案 B の `created_org_id` 未追加 → 後付け RLS 時にバックフィル必要 | **取り込み済** (案 A から先食い、§2) |
| 案 B の `geometry_type` 分離未対応 → 既存 feature 再整形負債 | **取り込み済** (案 A から先食い、§2) |
| 案 B の `layer_import_job` 不採用 → 進捗 API への発展で新テーブル必要 | **取り込み済** (同期完結のまま 1 行だけ書く運用) |
| 補強 2: `sourceFormat` を API 契約に明示 | 採用。`POST /api/admin/layers/{id}/features:bulk` のリクエスト body に `sourceFormat: string` を含める |
| 補強 3: `InferredSchemaFieldDto` を WinForms 内部 DTO として導入 | 採用。API/DB 契約は親型 `SchemaFieldDto` に縮退、WinForms 内部で `sampleValues[] / nullable / defaultValue` を保持 |
| 補強 4: `fn_feature_bulk_insert` を Phase B でスタブだけ作る | **不採用**。実装リスクレビュー A-地雷 2 (audit バッチ違反) と緊張する。Phase C で要件が明確になってから設計 |
| 補強 5: GDAL を Docker サイドカーへ逃がす経路を申し送り | 採用。`docs/layer-import.md` の Phase C 申し送りセクションに明記 |

### 5.2 実装リスクレビュー

| 指摘 | 対応 |
|---|---|
| 1. audit_log の粒度ポリシー (案 A 致命) | 案 A の B104 を **却下**。Phase B は `fn_feature_insert` 1 件 1 audit を維持 |
| 2. `fn_feature_insert` のバルク呼び出し時 `p_actor` / `p_request_id` 規約 | **Design 段階で確定**: `p_request_id` は **チャンク単位で同一 UUID** (1000 件 = 1 request_id)、`p_actor` は **現在ユーザの UUID をそのまま** (擬似値は使わない)。`fn_feature_insert` のシグネチャは変更しない |
| 3. `feature_current.layer_id` FK と `layers.deleted_at` の整合 (案 C 致命) | **Phase B 内で確認 + 修正**: `FeatureEndpoints` の `GET /api/features` に `WHERE l.deleted_at IS NULL` が無いことが判明したら、B4 のスコープに含める (実装コストは 0.5 人日以下、案 C のように仮定しない) |
| 4. Npgsql CommandTimeout と chunk サイズの実測 | **Design 段階で実測スパイク**: 1 時間枠で `db/fixtures/perf/` 用 5000 件 GeoJSON を作り、`fn_feature_insert × N` の所要時間を測定。デフォルト `CommandTimeout=30s` を超える場合は WinForms 側 chunk サイズを 500 に下げる (タイムアウトを伸ばすより chunk を細かくする) |
| 5. MIF/TAB の和歌山測地系実機検証 | **Phase B では対象外なので不要** (Phase C 着手前に実機検証タスクを 1 件切る) |
| 6. 同時編集競合の楽観ロック粒度 | **Phase B では layer 単位ロックを採用**: `layer_import_job.status='running'` の間は同 layer への `POST /api/features` を 409 で弾く。実装は `AdminLayersEndpoints.cs` のミドルウェア層 (0.3 人日) |
| 7. `SHAPE_ENCODING` の設定スコープ (案 C 致命) | **Phase B 対象外**。Phase C で GDAL 投入時は **DataSource Open オプション** (`gdal:open_options=ENCODING=CP932`) を使う方針を申し送り |
| 8. `ILayerSource` の async 化 | **Phase B から `IAsyncEnumerable<>` 統一**: `ReadFeaturesAsync` は既に async。`InferSchemaAsync` も `Task<IReadOnlyList<InferredField>>` に変更。Phase C 破壊変更ゼロ |

### 5.3 テスタビリティレビュー

| 指摘 | 対応 |
|---|---|
| B-T1: `ILayerSource` 契約テストが Phase B 中に検証不能 | **`ILayerSourceContractTests<T>` ジェネリック抽象クラス**を Phase B で書く (補強 B-S1)。`GeoJsonLayerSourceTests` と `CsvLayerSourceTests` で先に走らせ、Phase C `GdalLayerSourceTests` 追加時に契約一貫性を自動検証 |
| B-T2: CSV `source_srid` API 側 `ST_Transform` の検証コスト | **WinForms 側 `ProjNet` で 4326 化** に変更 (補強 B-S2)。API 入力は常に 4326 GeoJSON、bulk endpoint の `source_srid` 分岐削除。SRID 変換は純粋関数テスト |
| B-T3: 「1 万件超は Phase C」のテストが境界条件しか書けない | **設定値ベース** (`appsettings.json: BulkInsertMaxCount=5000`、補強 B-S3)。境界テスト 2 ケース (`5000 → 200` / `5001 → 413`) に集約。Phase C 上限解除時はパラメタ追従 |
| 共通-S1: スキーマ推論を `IInferenceStrategy` で純粋関数化 | 採用。`InferenceStrategies/GeoJsonInferenceStrategy.cs` / `CsvInferenceStrategy.cs` を切り出し、入力 (`JsonElement` / `string[][]`) → 出力 (`InferredField[]`) で 100% メモリ完結 |
| 共通-S2: 認可マトリクステストを `[Theory]` で生成 | 採用。A506 のパターンを継承し `admin/general/guest × CRUD + bulk = 15 ケース` を `[Theory]` |
| C-S2 (ViewModel 切り出し) | **部分採用**: `ImportWizardViewModel` を切り出し、Step1〜3 の状態遷移ロジックを ViewModel に寄せる。`Form` 側は ViewModel をバインドするだけにし、ヘッドレステストを可能にする |

## 6. 採択案 案 P の完全な詳細

### 6.1 データモデル変更

| ID | 内容 | 種類 |
|---|---|---|
| P101 | `layers` 拡張: `description TEXT NULL`, `source_format TEXT NULL CHECK (source_format IN ('geojson','csv','shapefile','mif','tab') OR source_format IS NULL)`, `source_srid INT NULL`, `geometry_type TEXT NULL` (`Point/LineString/Polygon/MultiPoint/MultiLineString/MultiPolygon/GeometryCollection`), `created_by UUID NOT NULL REFERENCES users(id)`, `created_org_id INT NOT NULL REFERENCES organizations(id)`, `deleted_at TIMESTAMPTZ NULL`, `updated_at TIMESTAMPTZ NOT NULL DEFAULT now()` | DDL |
| P102 | `fn_layer_create(p_name, p_layer_type, p_geometry_type, p_source_format, p_source_srid, p_description, p_schema_json, p_actor, p_request_id, p_user_id, p_org_id) RETURNS INT` | 関数 |
| P103 | `fn_layer_delete(p_layer_id, p_actor, p_request_id, p_user_id, p_org_id) RETURNS VOID` — `layers.deleted_at` を立てるだけ。feature_current は据え置き (案 C 論点 5 採用、`FeatureEndpoints` 側で `deleted_at IS NULL` フィルタ強制) | 関数 |
| P104 | `layer_import_job` テーブル新設: `job_id UUID PK`, `layer_id INT FK REFERENCES layers(layer_id)`, `status TEXT CHECK (status IN ('running','succeeded','failed'))`, `total_count INT NULL`, `inserted_count INT NOT NULL DEFAULT 0`, `started_at TIMESTAMPTZ NOT NULL DEFAULT now()`, `finished_at TIMESTAMPTZ NULL`, `created_by UUID NOT NULL`, `created_org_id INT NOT NULL`, `error_text TEXT NULL` | DDL |

**バルク insert 用の新規 PL/pgSQL 関数は作らない**。API 側 Tx ループで `fn_feature_insert` を呼ぶ (案 B 論点 1)。これにより:
- audit_log の 1 件 1 行原則が守られバイテンポラル整合
- `fn_feature_insert` のシグネチャを変えず Phase A 規約を破壊しない
- DB 関数の追加コスト = 2 個 (`fn_layer_create` / `fn_layer_delete`) + テーブル 1 個 (`layer_import_job`)

### 6.2 API endpoint

新規グループ `/api/admin/layers` (`MapGroup` + `RequireRole("admin")`)。

| メソッド | パス | 役割 | リクエスト | 成功 | 失敗 |
|---|---|---|---|---|---|
| GET | `/api/admin/layers` | admin 用一覧 | `?includeDeleted=false` | 200 `LayerAdminDto[]` | 401/403 |
| POST | `/api/admin/layers` | 空レイヤ作成 | `CreateLayerRequestDto { name, layerType, geometryType, sourceFormat, sourceSrid, description, schemaJson }` | 201 `LayerAdminDto` | 400/401/403 |
| GET | `/api/admin/layers/{id}` | 単体取得 | — | 200 / 404 | — |
| PATCH | `/api/admin/layers/{id}` | name/description/layerType 部分更新 | `UpdateLayerRequestDto` | 200 | 400/404 |
| DELETE | `/api/admin/layers/{id}` | 論理削除 (`fn_layer_delete`) | — | 204 | 404/409 (`layer_import_job.status='running'` 時) |
| POST | `/api/admin/layers/{id}/import-jobs` | ジョブ開始 | `{ totalCount }` | 201 `ImportJobDto { jobId }` | 400/409 |
| POST | `/api/admin/layers/{id}/features:bulk` | バルク投入 (チャンク 1 回) | `BulkFeaturesRequestDto { jobId, chunkOrdinal, chunkTotal, sourceFormat, features: GeoJsonFeature[] }` | 200 `BulkFeaturesResponseDto { insertedCount, featureIds[] }` | 400/404/409/413 |
| POST | `/api/admin/layers/import-jobs/{jobId}/finalize` | ジョブ完了通知 | `{ status: "succeeded" \| "failed", errorText? }` | 200 | 404/409 |
| GET | `/api/admin/layers/import-jobs/{jobId}` | 進捗参照 | — | 200 `ImportJobDto` | 404 |

**既存 `PUT /api/admin/layers/{id}/schema` は残置** (案 C 論点 12)。

`appsettings.json` 設定: `BulkInsert: { MaxCountPerChunk: 5000, ChunkDefaultSize: 1000 }`。413 は `features.Count > MaxCountPerChunk` で返す。

### 6.3 PL/pgSQL 関数の追加

#### P102: `fn_layer_create`
- `INSERT INTO layers (...) RETURNING layer_id`
- `INSERT INTO layer_schema_version (...)` (Phase A 規約継承)
- `INSERT INTO audit_log (action='layer_create', after_doc=to_jsonb(new_row), ...)`

#### P103: `fn_layer_delete`
- `UPDATE layers SET deleted_at=now() WHERE layer_id=p_layer_id AND deleted_at IS NULL`
- 0 行更新時は `RAISE EXCEPTION 'layer not found or already deleted'`
- `INSERT INTO audit_log (action='layer_delete', ...)`
- feature は触らない (案 C 論点 5)

#### バルク投入の流れ (新規 DB 関数なし)
API 側で:
```csharp
await using var tx = await conn.BeginTransactionAsync(ct);
var requestId = Guid.NewGuid(); // チャンク単位で一意
foreach (var feature in req.Features) {
    await conn.ExecuteScalarAsync<long>(
      "SELECT fn_feature_insert(@layerId, @geom, @attrs, @actor, @requestId, @userId, @orgId)",
      new { layerId, geom = feature.Geometry, attrs = feature.Properties,
            actor = currentUser.Id, requestId, userId = currentUser.Id, orgId = currentUser.OrgId },
      tx);
}
await tx.CommitAsync(ct);
await UpdateImportJobProgress(jobId, insertedCount: req.Features.Count, ct);
```

### 6.4 WinForms UI フロー

```
LayerAdminForm (Modal, MainForm からは Show() で独立起動)
 ├─ LayerListGrid (DataGridView, GET /api/admin/layers)
 ├─ ToolStrip [新規インポート] [編集] [削除]
 └─ ImportWizardDialog (Modal) ← ImportWizardViewModel をバインド
      ├─ Step1: SourceFormatPicker (Phase B: GeoJSON/CSV のみ有効、SHP/MIF/TAB は「Phase C 対応予定」ラベル付き)
      │         + SridConfirm (CSV のみユーザ指定、GeoJSON は 4326 固定)
      │         + CSV の lat/lng 列 ComboBox (自動推測 + ユーザ確定)
      ├─ Step2: SchemaGrid (UserControl、AttributeEditorControl と共用 = H4 解消)
      └─ Step3: 投入実行
          ├─ POST /api/admin/layers (空レイヤ作成)
          ├─ POST /api/admin/layers/{id}/import-jobs (jobId 取得)
          ├─ ProjNet で 4326 化 (CSV のみ) → 1000 件 chunk
          ├─ POST .../features:bulk × N回 (chunkOrdinal 加算、ProgressBar 更新)
          ├─ POST .../import-jobs/{jobId}/finalize { status: "succeeded" }
          └─ キャンセル時: finalize { status: "failed", errorText } のみ
                          (feature_current は据え置き、ユーザに「削除しますか?」確認 → DELETE /api/admin/layers/{id})
```

`ImportWizardViewModel` を切り出し、Form 側は ViewModel をバインドするだけ。状態遷移ロジック (`CanGoNext` / `CanGoBack` / `IsImporting`) は ViewModel 内で純粋関数化、ヘッドレステスト可能。

### 6.5 GDAL 依存

**Phase B では GDAL を一切使わない**。

| ライブラリ | 用途 |
|---|---|
| `NetTopologySuite.IO.GeoJSON4STJ` | GeoJSON パース |
| `ProjNet` | SRID 変換 (CSV のみ、4326 化) |
| `System.IO` (手書き) | CSV パース (lat/lng 列抽出) |

`windos-app.csproj` の `RuntimeIdentifier` 設定なし。CI 上で `windos-app.tests` は Linux ランナーでも実行可能。

Phase C 申し送り: `MaxRev.Gdal.Core` + `MaxRev.Gdal.WindowsRuntime.Minimal` を追加し `GdalLayerSource : ILayerSource` を実装。**`SHAPE_ENCODING` はプロセス環境変数ではなく `Ogr.Open(path, options)` の `ENCODING=CP932` オプションで指定**。

### 6.6 スキーマ推論ロジック

`IInferenceStrategy` インタフェースで形式別に純粋関数化 (テスタビリティレビュー 共通-S1)。

```csharp
interface IInferenceStrategy {
    string SourceFormat { get; }
    Task<IReadOnlyList<InferredField>> InferAsync(Stream input, CancellationToken ct);
}

class GeoJsonInferenceStrategy : IInferenceStrategy {
    // properties の最初の 100 feature から JsonValueKind 直接マップ
    // Number → "number" (整数のみなら "integer")
    // String → ISO8601 date 試行 → "string" or "date"
    // True/False → "boolean"
    // null 検出 → nullable=true (InferredField 内部のみ保持)
}

class CsvInferenceStrategy : IInferenceStrategy {
    // 100 行サンプリングで integer → number → boolean → date → string の順に試行
    // 全列で null/空文字あれば nullable=true
}
```

WinForms 内部 DTO `InferredField { name, type, required, nullable, defaultValue, sampleValues[] }` (拡張性レビュー 補強 3)。API 送信時に親型 `SchemaFieldDto { name, type, required }` に縮退。

### 6.7 ILayerSource インタフェース (async 統一)

```csharp
interface ILayerSource : IAsyncDisposable {
    string SourceFormat { get; }       // "geojson" | "csv"
    int? SourceSrid { get; }            // GeoJSON=4326, CSV=null (要ユーザ指定)
    Task<IReadOnlyList<InferredField>> InferSchemaAsync(CancellationToken ct);
    IAsyncEnumerable<GeoJsonFeature> ReadFeaturesAsync(int sourceSrid, CancellationToken ct);
}
```

Phase C 受け渡し: `GdalLayerSource` を追加するだけ。`ReadFeaturesAsync` 内で OGR `OGRCoordinateTransformation` を呼ぶか `ProjNet` を呼ぶかは実装詳細に閉じる。

### 6.8 工数見積 (人日)

| ID | タイトル | 見積 |
|---|---|---|
| P101 | layers 拡張 migration | 0.5 |
| P102 | fn_layer_create | 0.5 |
| P103 | fn_layer_delete | 0.5 |
| P104 | layer_import_job テーブル + DTO | 0.5 |
| P201 | LayerAdminEndpoints CRUD + JsonOpts 集約 (H2 解消) | 1.5 |
| P202 | bulk endpoint + import-jobs endpoint + 409 ロック | 1.5 |
| P203 | FeatureEndpoints の `deleted_at IS NULL` フィルタ確認/追加 | 0.5 |
| P301 | LayerAdminForm 骨格 + MainForm メニュー 1 行追加 | 1.0 |
| P302 | ImportWizardForm + ImportWizardViewModel (Step1〜3) | 2.0 |
| P303 | ILayerSource + GeoJsonLayerSource + CsvLayerSource (async 統一) | 1.5 |
| P304 | IInferenceStrategy 切り出し + 共通 SchemaGrid (H4 解消) | 1.5 |
| P305 | ProjNet SRID 変換ヘルパ + chunk 投入ロジック | 1.0 |
| P401 | api.tests 認可マトリクス [Theory] + audit_log + 409 | 1.5 |
| P402 | windos-app.tests: ILayerSourceContractTests<T> + InferenceStrategy 純粋関数 | 1.5 |
| P403 | パフォーマンス実測スパイク (5000 件 GeoJSON 投入時間計測) | 0.5 |
| P501 | docs/layer-import.md + auth.md + Phase C 申し送り | 1.0 |
| **合計** | | **17.0** |

Plan 見積 14〜18 人日の範囲内。案 B の 9〜12 人日からは増加しているが、これは:
- 案 A から取り込んだ DB 投資 (P104 / 案 A 由来の `created_org_id` / `geometry_type`)
- レビュー反映で追加した品質投資 (P203 FK 整合確認 / P402 契約テスト / P403 性能実測 / ViewModel 切り出し)

の合算。Phase C 追加見積は **5〜7 人日** (Shapefile/MIF/TAB の GDAL 経由実装) で変わらず。

### 6.9 Review② 負債合流

| 負債 ID | 案 P での扱い |
|---|---|
| H2 (JsonSerializerOptions 重複) | ◯ P201 で `api/Json/JsonOpts.cs` に集約 |
| H4 (AttributeEditorControl.ParentForm キャスト) | ◯ P304 で `SchemaGrid` UserControl 切り出し + `IFeatureSaveCoordinator` 経由に変更 |
| H5 (MainForm god class) | × Phase B 持ち越し (案 B/C と同じ判断、スコープ拡大を避ける) |

### 6.10 設計論点 15 件への回答サマリ

| # | 論点 | 案 P の採択 |
|---|---|---|
| 1 | バルク insert 戦略 | API Tx ループ `fn_feature_insert × N` (案 B/C)。専用関数は Phase C 申し送り |
| 2 | SRID 変換責務 | **WinForms 側 `ProjNet` で 4326 化** (案 A 採用 + GDAL 非依存化) |
| 3 | 型推論優先順位 | `IInferenceStrategy` で形式別純粋関数化 (補強適用) |
| 4 | .dbf 文字コード | **Phase B 対象外** (SHP 非対応)。Phase C で `DataSource Open options` 方式 |
| 5 | layer 論理削除と feature | (a) `layers.deleted_at` のみ立てる (案 B/C)。`FeatureEndpoints` の `deleted_at IS NULL` フィルタを P203 で確認/追加 |
| 6 | layer_type の意味 | **`layer_type` (用途ラベル) と `geometry_type` (ジオメトリ型) を分離** (案 A 採用) |
| 7 | SRID 列の持ち方 | (a) `layers.source_srid` (案 A/B/C 共通) |
| 8 | fields 命名衝突 | `InferredField` 内部 DTO + API 送信時 `SchemaFieldDto` 縮退 (案 A 採用) |
| 9 | 大ファイル境界 | (a) WinForms チャンク 1000 件逐次 POST。**`BulkInsertMaxCount=5000` を設定値化** (Phase C 上限解除時にテストパラメタ自動追従) |
| 10 | 進捗とロールバック | **`layer_import_job` テーブルで観測 + finalize で完了通知** (案 A 採用、ただし同期完結)。途中失敗時は WinForms から `DELETE /api/admin/layers/{id}` をユーザに促す |
| 11 | 起動権限チェック | (a)+(c) MainForm でメニュー Visible 制御 + サーバ側 `RequireRole("admin")` 2 重防御 (案 C) |
| 12 | 既存 schema PUT | **残置 + PATCH と棲み分け** (案 C 採用) |
| 13 | MapInfo TAB | **Phase B 対象外** (Phase C で zip 必須) |
| 14 | CSV geom 列 UI | (a) 自動推測 + Step1 で確定 (案 B/C 共通)。WKT 列は Phase C |
| 15 | fixture 配置 | `windos-app.tests/Fixtures/import/` (案 B/C 共通)。Phase B は `geojson_point.json` / `csv_latlng.csv` のみ |

## 7. ファイル別差分マップ (Issue 化の入力)

### DB (db/migration/)
- `010_layers_extend.sql` (P101)
- `011_fn_layer_create.sql` (P102)
- `012_fn_layer_delete.sql` (P103)
- `013_layer_import_job.sql` (P104)

### API (api/)
- `Endpoints/AdminLayersEndpoints.cs` (P201, P202, 新規)
- `Endpoints/FeatureEndpoints.cs` (P203, `deleted_at IS NULL` フィルタ確認/追加)
- `Json/JsonOpts.cs` (P201, H2 解消、新規)
- `Program.cs` (`MapGroup("/api/admin/layers").RequireRole("admin")` 追加)
- `Dtos/LayerAdminDto.cs` / `CreateLayerRequestDto.cs` / `UpdateLayerRequestDto.cs` / `BulkFeaturesRequestDto.cs` / `ImportJobDto.cs` (新規)
- `appsettings.json` (`BulkInsert` セクション追加)

### WinForms (windos-app/)
- `Forms/LayerAdminForm.cs` + `.Designer.cs` (P301, 新規)
- `Forms/ImportWizardForm.cs` + `.Designer.cs` (P302, 新規)
- `ViewModels/ImportWizardViewModel.cs` (P302, 新規、ヘッドレステスト対象)
- `Controls/SchemaGrid.cs` + `.Designer.cs` (P304, H4 解消、新規)
- `Controls/AttributeEditorControl.cs` (P304, ParentForm キャストを `IFeatureSaveCoordinator` 経由に変更)
- `Services/ILayerSource.cs` / `GeoJsonLayerSource.cs` / `CsvLayerSource.cs` (P303, 新規)
- `Services/IInferenceStrategy.cs` / `GeoJsonInferenceStrategy.cs` / `CsvInferenceStrategy.cs` (P304, 新規)
- `Services/SridConverter.cs` (P305, ProjNet ラッパ、新規)
- `Services/ApiClient.cs` (既存に追記: Create/Update/Delete Layer + StartImportJob + BulkInsertFeatures + FinalizeImportJob)
- `Forms/MainForm.cs` (1 行追加 + Visible 制御)
- `windos-app.csproj`: `NetTopologySuite.IO.GeoJSON4STJ` + `ProjNet` 追加

### Tests
- `api.tests/AdminLayersEndpointsTests.cs` (P401, 認可マトリクス [Theory] + 409 + audit_log)
- `windos-app.tests/Services/ILayerSourceContractTests.cs` (P402, ジェネリック抽象)
- `windos-app.tests/Services/GeoJsonLayerSourceTests.cs` / `CsvLayerSourceTests.cs` (P402)
- `windos-app.tests/Services/GeoJsonInferenceStrategyTests.cs` / `CsvInferenceStrategyTests.cs` (P402, 純粋関数 [Theory])
- `windos-app.tests/ViewModels/ImportWizardViewModelTests.cs` (P402, ヘッドレス状態遷移)
- `windos-app.tests/Fixtures/import/geojson_point.json` + `csv_latlng.csv` (P402)
- `api.tests/Performance/BulkInsertSpike.cs` (P403, 5000 件投入時間計測、CI では `[Trait("Category","Performance")]` で除外可)

### Docs
- `docs/layer-import.md` (P501, 新規。Phase B 範囲 + Phase C 申し送り + GDAL サイドカー経路の選択肢明記)
- `docs/auth.md` (P501, `/api/admin/layers*` ロールマトリクス追記)
- `docs/PHASE_B_INDEX.md` (P501, Issue 一覧へのリンク)

## 8. リスクと緩和

| リスク | 緩和策 |
|---|---|
| P403 の性能実測で `fn_feature_insert × 1000` が 30 秒超 | chunk サイズを 500 に下げる (`appsettings.json` の `ChunkDefaultSize` 変更のみ)。`fn_feature_insert` 自体の最適化は Phase C |
| Phase B 出荷後に「SHP が無いと使えない」現場拒否 | LayerAdminForm Step1 で SHP/MIF/TAB を「Phase C 対応予定」と明示。QGIS による GeoJSON 変換ガイドを `docs/layer-import.md` に記載 |
| P203 で `FeatureEndpoints` 修正が予想以上に大きい | 0.5 人日を超えたら Issue を分割し別 PR にする。`deleted_at IS NULL` を `WHERE` に追加するだけのはず |
| `ImportWizardViewModel` 切り出しが WinForms データバインディングと相性悪い | INotifyPropertyChanged ベースで実装。Phase A の LoginForm に類似実装あれば踏襲、無ければ手書き |
| `layer_import_job` の status 遷移バグ | enum + 遷移マトリクスを C# 側で純粋関数化しテスト (拡張性 補強 + テスタビリティ A-S2 を踏襲) |

## 9. Phase C 申し送り

1. `GdalLayerSource : ILayerSource` の追加実装 (5〜7 人日)
2. **`SHAPE_ENCODING` はプロセス環境変数ではなく `Ogr.Open(path, options)` で渡す** (実装リスク C-地雷 1 回避)
3. MIF/TAB の和歌山測地系実機検証タスク (1 人日)
4. `BulkInsertMaxCount` 上限解除 + `fn_feature_bulk_insert` 専用関数の必要性判断 (audit_log バッチ集約は Phase C でも採用しない方針)
5. GDAL を Docker サイドカーへ逃がす経路の選択肢 (`RemoteGdalLayerSource`) を WebGIS 連携要件が立った時に再評価
6. H5 (MainForm god class) の本格分割は Phase C 以降の独立サイクルへ
7. `layer_import_job` の非同期化 (バックグラウンドジョブ + 進捗 polling API) は Phase D 以降

## 10. 次ステップ

- 本案 P を入力に Issue 化フェーズへ進む (`docs/issues/PHASE_B_ISSUES_INDEX.md`)
- WBS P101〜P501 を 1 Issue 1 PR でラベル付け (`phase:B`, `area:db/api/winforms/tests/docs`, `negotiable-debt:H2|H4`)
- **stacked PR pitfall** (MEMORY.md) に従い、すべての PR の `base=main` で固定
- P403 (性能実測スパイク) を最初の 1 Issue として立て、結果次第で P202 / P305 の chunk サイズ既定値を確定
