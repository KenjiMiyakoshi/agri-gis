# agri-gis Phase B Design A — 業界標準フル装備案

Phase B Plan (`PHASE_B_PLAN.md`) を入力に、設計論点 15 件すべてに「最初から本気で組む」スタンスで回答する設計案。スコープ縮減 (案 B) や段階導入 (案 C) は別文書で扱う。

## A 案コンセプト

- 対応 5 形式 (Shapefile zip / GeoJSON / CSV / MIF+MID / TAB) を **初版から全部** 対応
- GDAL/OGR を WinForms 側で完全活用 (`MaxRev.Gdal.Core` + `MaxRev.Gdal.WindowsRuntime.Minimal`)。API/Docker 側は GDAL 非依存
- バルク insert は **専用 PL/pgSQL 関数** (`fn_feature_bulk_insert(JSONB)`) + **API 側でチャンク分割 (1000 件)** のハイブリッド。10 万 feature の Shapefile を 5 分以内で投入することを性能目標
- WinForms はドラッグ&ドロップ + 3 ステップウィザード + プログレスバー + キャンセル可
- SRID 検出 / EPSG:4326 変換 / 文字コード推定 (UCSDet + CP932 フォールバック) は WinForms 側で完結
- 投入失敗時は **レイヤごとロールバック** (作成自体を取り消す) を既定、`?keepLayer=true` で部分残しも可

## データモデル変更

### 新規 migration (B-prefix で連番化)

| ID | 内容 | 種類 |
|---|---|---|
| B101 | `layers` 拡張: `description TEXT`, `source_srid INT`, `geometry_type TEXT`, `created_by UUID NOT NULL`, `created_org_id INT NOT NULL`, `deleted_at TIMESTAMP NULL`, `updated_at TIMESTAMP NOT NULL DEFAULT now()` | DDL |
| B102 | `fn_layer_create(p_name, p_geometry_type, p_source_srid, p_description, p_schema_json, p_actor, p_request_id, p_user_id, p_org_id) RETURNS INT` | 関数 |
| B103 | `fn_layer_delete(p_layer_id, p_actor, p_request_id, p_user_id, p_org_id) RETURNS VOID` — `layers.deleted_at` 立て、`feature_current` を `feature_history` へ一括移送 | 関数 |
| B104 | `fn_feature_bulk_insert(p_layer_id INT, p_features JSONB, p_actor, p_request_id, p_user_id, p_org_id) RETURNS INT[]` — JSONB 配列 (GeoJSON FeatureCollection の features 部分) を受けてループ内 `INSERT`。audit_log は **バッチ単位 1 行** (`action='feature_bulk_insert'`, `after_doc` に件数と feature_id 範囲) | 関数 |
| B105 | `layer_import_job` テーブル新設: `job_id UUID PK`, `layer_id INT FK`, `status TEXT ('running'/'succeeded'/'failed'/'rolled_back')`, `total_count INT`, `inserted_count INT`, `started_at`, `finished_at`, `created_by UUID`, `error_text TEXT` — 投入の途中状態を観測可能にする | DDL |

#### 決めうち判断
- **論点 5 (layer 論理削除と feature)**: B 案 (feature 一括 history 移送) を採用。バイテンポラル原則 (履歴保持) と整合し、再表示要求にも応えられる。`fn_layer_delete` 内で `INSERT INTO feature_history ... SELECT FROM feature_current WHERE layer_id = p_layer_id` + `DELETE FROM feature_current` を 1 トランザクションで実行
- **論点 6 (layer_type の意味)**: B 案を採用。`layer_type` は用途ラベル (`field` / `observation` / 自由テキスト) として残し、ジオメトリ型は新規 `geometry_type TEXT` 列 (`Point` / `LineString` / `Polygon` / `MultiPoint` / `MultiLineString` / `MultiPolygon` / `GeometryCollection`) に分離。`feature_current.geom` の列定義は `geometry(Geometry, 3857)` のまま (動的型制約はマイグレーション複雑度に見合わない)
- **論点 7 (SRID 列)**: A 案 (`layers.source_srid` に元 SRID を記録、feature には載せない)。WinForms が EPSG:4326 に変換済みのものを送るため feature 側冗長

## API endpoint 詳細

新規グループ `/api/admin/layers` (`MapGroup` + `RequireRole("admin")`)。既存 `PUT /api/admin/layers/{id}/schema` は **下位互換のため残す** が、新 `PATCH /api/admin/layers/{id}` でも schema_json を受け付ける (論点 12)。

| メソッド | パス | 役割 | リクエスト | 成功 | 失敗 |
|---|---|---|---|---|---|
| GET | `/api/admin/layers` | admin 用一覧 (`deleted_at` 含む) | `?includeDeleted=false` | 200 `LayerAdminDto[]` | 401/403 |
| POST | `/api/admin/layers` | 空レイヤ作成 (インポート前段) | `CreateLayerRequestDto { name, geometryType, sourceSrid, description, schemaJson }` | 201 `LayerAdminDto` + `Location` | 400/401/403 |
| GET | `/api/admin/layers/{id}` | 単体 | — | 200 / 404 | — |
| PATCH | `/api/admin/layers/{id}` | name/description/schemaJson 部分更新 | `UpdateLayerRequestDto` | 200 | 400/404 |
| DELETE | `/api/admin/layers/{id}` | 論理削除 (`fn_layer_delete`) | — | 204 | 404 |
| POST | `/api/admin/layers/{id}/features:bulk` | バルク投入 | `BulkFeaturesRequestDto { features: GeoJSONFeature[], chunkOrdinal: int, chunkTotal: int, jobId: UUID }` | 200 `BulkFeaturesResponseDto { insertedCount, featureIds[] }` | 400/404/409 |
| POST | `/api/admin/layers/{id}/import-jobs` | ジョブ開始通知 (total_count 確定) | `{ totalCount }` | 201 `{ jobId }` | 400 |
| GET | `/api/admin/layers/import-jobs/{jobId}` | 進捗参照 | — | 200 `ImportJobDto` | 404 |
| POST | `/api/admin/layers/import-jobs/{jobId}/finalize` | 確定 (status を succeeded へ) または rollback (`fn_layer_delete` + status='rolled_back') | `{ commit: bool }` | 200 | 404/409 |

#### 決めうち判断
- **論点 1 (バルク insert 戦略)**: 案 (b) を主、案 (c) は不採用。理由: `COPY` は audit_log を 1 行も書かないため、バイテンポラル + 監査の原則と矛盾。`fn_feature_bulk_insert` がループ内で `feature_current` に INSERT し、最後に audit_log を **バッチ 1 行** (件数とジョブ ID 記録) で出す
- **論点 9 (大ファイル投入の境界)**: 案 (a) を採用。WinForms が 1000 feature ずつ JSONB に詰めて逐次 POST。チャンク順序は `chunkOrdinal` で server 側がトラッキング、欠番検出可。Multipart ストリーム (b) は ASP.NET Core Minimal API + GDAL→API の責務分離を崩すので不採用
- **論点 10 (進捗とロールバック)**: 案 (a)+(c) ハイブリッド。`layer_import_job` テーブルで再開可能なジョブ ID を持ちつつ、`finalize { commit: false }` でレイヤごと取り消す。途中での feature 単位 rollback (b) はやらない (バルク insert の利点を打ち消すため)
- **論点 11 (起動権限チェック)**: 案 (a)+(b) 併用。MainForm 起動時に admin で無ければメニュー非表示、開く瞬間にも再チェックし 401 時は `LoginForm` へ。サーバ任せ (c) は UX が悪い
- **論点 12 (既存 schema PUT との関係)**: 残置 + 統合。`PUT /api/admin/layers/{id}/schema` は維持、`PATCH /api/admin/layers/{id}` に `schemaJson` フィールドを足し内部実装は `fn_layer_schema_upsert` を共通呼び出し。docs/auth.md ロールマトリクスは PATCH 行を追記

## PL/pgSQL 関数の追加・拡張

### B102: `fn_layer_create`
- `INSERT INTO layers (...) RETURNING layer_id`
- 初期 `schema_version=1`、`layer_schema_version` に append
- audit_log に `action='layer_create'`, `before_doc=NULL`, `after_doc=to_jsonb(new_row)` を出力

### B103: `fn_layer_delete`
- `UPDATE layers SET deleted_at=now()`
- `INSERT INTO feature_history SELECT ... FROM feature_current WHERE layer_id=p_layer_id` + `DELETE FROM feature_current ...`
- audit_log は 1 行 (`action='layer_delete'`, `after_doc` に削除 feature 件数)

### B104: `fn_feature_bulk_insert`
```
FOR f IN SELECT * FROM jsonb_array_elements(p_features) LOOP
    -- 既存 fn_feature_insert と同じ変換ロジック (ST_GeomFromGeoJSON + ST_Transform)
    INSERT INTO feature_current (...) RETURNING feature_id INTO v_id;
    v_ids := array_append(v_ids, v_id);
END LOOP;
INSERT INTO audit_log (... action='feature_bulk_insert', after_doc=jsonb_build_object(
    'inserted_count', array_length(v_ids,1),
    'min_feature_id', v_ids[1],
    'max_feature_id', v_ids[array_length(v_ids,1)],
    'job_id', p_job_id
));
```
- audit_log を 1 行に抑えることで 10 万件投入時の audit_log サイズ爆発を回避 (1 row × チャンク数)
- 既存 `fn_feature_insert` は単体追加用に残置 (API の `POST /api/features` で使用)

## WinForms UI フロー

### LayerAdminForm 構成
- `MenuStrip`: `[管理] -> [レイヤ管理]` (admin のみ表示)。MainForm から `using var f = _sp.GetRequiredService<LayerAdminForm>(); f.ShowDialog(this);`
- レイアウト (SplitContainer):
  - 左: `DataGridView` (既存レイヤ一覧、`deleted_at` 含む / フィルタ checkbox)
  - 右: 選択レイヤの詳細 (name/description/geometry_type/schema_json プレビュー + 「新規インポート」「編集」「削除」ボタン)
- 上部に **D&D 受け取り Panel** (ファイル/zip ドロップで `ImportWizardForm` を起動)

### ImportWizardForm (Modal, 3 ステップ)

```
Step 1: 取り込み元の確認
  - 検出形式 (Shapefile/GeoJSON/CSV/MIF/TAB) と OGR DataSource 概要
  - SRID 自動検出結果 + 手動上書き ComboBox (EPSG:4326/3857/6677/...の和歌山測地系プリセット)
  - 文字コード自動判定結果 (UCSDet) + 上書き (UTF-8/CP932/Shift_JIS)
  - CSV のみ: lat/lng 列の自動推測 + 上書き UI、または WKT 列指定
  - レイヤ名 / 説明 / layer_type ラベル入力
  - [次へ]

Step 2: スキーマ確認
  - DataGridView: column_name | type | required | nullable | sample_values
  - AttributeEditorControl の DataGridView 設定を流用 (H4 への対処として 共通基盤 SchemaGrid に切り出し: 後述 H4 改善)
  - geometry_type は OGR 検出結果を表示 (混在時は MultiXxx 正規化、ユーザに警告)
  - [戻る] [次へ]

Step 3: 投入実行
  - 投入総件数表示 + プログレスバー (チャンク単位)
  - 開始ボタン押下で:
      1) POST /api/admin/layers (空レイヤ作成、layer_id を取得)
      2) POST /api/admin/layers/{id}/import-jobs (jobId 取得)
      3) GDAL から feature を 1000 件ずつ取り出し、EPSG:4326 GeoJSON に変換
      4) POST /api/admin/layers/{id}/features:bulk を逐次 (chunkOrdinal を加算)
      5) 全チャンク完了で POST .../finalize { commit:true }
      6) キャンセル or 例外発生で finalize { commit:false } → サーバ側で fn_layer_delete
  - [キャンセル] (CancellationToken)
```

#### 決めうち判断
- **論点 2 (SRID 変換の責務)**: 案 (a) 採用。WinForms が GDAL `OGRCoordinateTransformation` で 4326 化して送る。CSV は元から 4326 想定 (lat/lng) なので変換不要。API/DB は 4326 入力前提に統一されコードが簡潔
- **論点 3 (スキーマ推論の型優先順位)**:
  - OGR ドライバ系 (Shapefile/MIF/TAB): `OFTInteger → integer`, `OFTInteger64 → integer`, `OFTReal → number`, `OFTDate → date`, `OFTDateTime → string` (ISO8601), `OFTString → string`
  - CSV/GeoJSON: 100 行サンプリングで `integer → number → boolean (true/false/1/0) → date (YYYY-MM-DD) → string` の順に試行。null/空文字混在は `nullable=true` で記録
  - `required` は推論では常に false。ユーザが Step2 で手動で true に
- **論点 4 (.dbf 文字コード)**: 案 (b) 採用。`.cpg` 同梱優先 → 無ければ ICU の UCSDet で先頭 1KB を判定 → 確信度低時は CP932 試行 → 化け検出で UTF-8 フォールバック → 最終的にユーザに Step1 で確認可能
- **論点 8 (fields 命名衝突)**: 既存 `SchemaFieldDto { key, type, required, label }` を継承拡張する import 専用 DTO `InferredSchemaFieldDto : SchemaFieldDto { nullable, defaultValue, sampleValues[] }`。投入確定時は親型に縮退して `fn_layer_schema_upsert` に渡す
- **論点 13 (MapInfo TAB の取り扱い)**: zip 受け取り必須 (Shapefile と同じ)。フォルダ選択は将来検討。GDAL `MITAB` ドライバは安定動作確認済 (GDAL 3.6+ で和歌山測地系も読める想定)。読めない場合は `ImportException` を Step1 で表示
- **論点 14 (CSV geom 列指定 UI)**: 案 (a)+(c) 採用。ヘッダから `lat/latitude/y` / `lng/lon/longitude/x` を自動推測、見つからなければ Step1 でユーザに 2 列または WKT 列を選ばせる ComboBox を出す

## GDAL 依存の取り扱い

- NuGet: `MaxRev.Gdal.Core` (managed wrapper) + `MaxRev.Gdal.WindowsRuntime.Minimal` (native dll, win-x64)
- `windos-app.csproj` に `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` 追記。配布時はネイティブ DLL を `runtimes/win-x64/native/` 配下に同梱
- 初期化: `WinForms.Program.Main` で `GdalBase.ConfigureAll()` を一度呼ぶ (Ogr/Osr/Gdal 一括登録)
- API 側 / Docker compose にはインストール不要。CI も WinForms.tests でのみ GDAL 必要 → `windos-app.tests.csproj` に同 NuGet 追加
- LayerAdminForm では `Ogr.Open(path)` → `Layer.GetNextFeature()` のループで GeoJSON 化

## スキーマ推論ロジック詳細

```
1. OGR で DataSource を開く (zip は /vsizip/ プレフィクス、CSV は OGR_CSV、GeoJSON は GeoJSON、MIF/TAB は MITAB)
2. Layer.GetSpatialRef() → SourceSrid を取得 (NULL なら Step1 でユーザ入力必須)
3. Layer.GetLayerDefn() の FieldDefn を列挙 → OFT* を内部型に写像 (上記論点 3)
4. CSV/GeoJSON の場合は最初の 100 feature を読み、推論を上書き (整数→実数昇格、null 検出による nullable=true)
5. Layer.GetGeomType() → geometry_type 文字列に正規化 (wkbPolygon → Polygon, wkbMultiPolygon → MultiPolygon, wkbUnknown → 100 件サンプリングで多数決)
6. 結果を InferredSchemaFieldDto[] として返し、Step2 の DataGridView にバインド
```

## 工数見積 (人日, 小数 1 桁)

| ID | タイトル | 見積 |
|---|---|---|
| B101 | layers 拡張 migration | 0.5 |
| B102 | fn_layer_create | 0.5 |
| B103 | fn_layer_delete (history 移送) | 1.0 |
| B104 | fn_feature_bulk_insert + audit batch | 1.5 |
| B105 | layer_import_job テーブル + ジョブ DTO | 0.5 |
| B201 | LayerAdminEndpoints CRUD (+ JsonOpts 集約 = H2 解消) | 1.5 |
| B202 | bulk endpoint + import-jobs endpoint | 1.5 |
| B301 | LayerAdminForm 骨格 + MainForm メニュー切り出し (H5 部分解消) | 1.5 |
| B302 | ImportWizardForm Step1 (GDAL Open + SRID/文字コード/CSV列) | 2.0 |
| B303 | ImportWizardForm Step2 (スキーマ推論 + 共通 SchemaGrid 切り出し = H4 解消) | 1.5 |
| B304 | ImportWizardForm Step3 (チャンク投入 + 進捗 + キャンセル + finalize) | 2.0 |
| B401 | api.tests 認可マトリクス + audit_log + ロールバック | 1.5 |
| B402 | windos-app.tests スキーマ推論単体 (5 形式 fixture) | 1.5 |
| B501 | docs/layer-import.md + auth.md 追記 + PHASE_B_INDEX.md | 1.0 |
| **合計** | | **18.0** |

Plan 見積 14〜18 人日の上限に着地。Review② 負債 H2/H4/H5 を吸収した分の上振れ。

## Review② 負債の合流方針

- **H2 (JsonSerializerOptions 重複)**: B201 で `api/Json/JsonOpts.cs` を新設し `FeatureEndpoints` / `AdminEndpoints` / 新 `LayerAdminEndpoints` から参照
- **H4 (AttributeEditorControl の ParentForm キャスト)**: B303 で `SchemaGrid` UserControl を切り出して `AttributeEditorControl` と `ImportWizardForm.Step2` の両方から使う。ついでに `Saved` イベントを `IFeatureSaveCoordinator` インターフェース経由に変える (ParentForm キャスト除去)
- **H5 (MainForm god class)**: B301 でメニュー定義を `MainFormMenu` パーシャルクラスに分離。`LayerAdminForm` 起動は `OpenLayerAdminCommand` クラスに委譲

## テスト fixture 配置

- **論点 15**: `windos-app.tests/Fixtures/import/` に統一配置。`shapefile_polygon.zip` / `geojson_point.json` / `csv_latlng.csv` / `mif_line.mif` + `.mid` / `tab_polygon.zip` の最小サンプル (各 5〜10 feature)
- バイナリ合計 100KB 程度を見込み → **git LFS は不採用**。直接 git に入れる
- 大規模性能テスト用の 10 万件 Shapefile はリポジトリには入れず、`db/fixtures/perf/` を `.gitignore` し、ローカル/CI で生成スクリプトを別途用意

## 設計論点 15 件への回答サマリ

| 論点 | 採択 | 短い理由 |
|---|---|---|
| 1. バルク insert 戦略 | (b) 専用 fn_feature_bulk_insert | audit 整合 + ループ性能 |
| 2. SRID 変換責務 | (a) WinForms 側で 4326 化 | API/DB をシンプルに統一 |
| 3. 型推論優先順位 | OGR は OFT 写像 / CSV は integer→number→boolean→date→string | 実装容易 + 既存 SchemaField と整合 |
| 4. dbf 文字コード | (b) cpg→UCSDet→CP932→UTF-8 fallback | 実環境 (CP932) と OSS (UTF-8) 双方対応 |
| 5. layer 論理削除と feature | (b) 一括 history 移送 | バイテンポラル原則 |
| 6. layer_type の意味 | (b) geometry_type を分離 | 後方互換性 + 意味分離 |
| 7. SRID 列の持ち方 | (a) layers.source_srid | feature 側冗長削減 |
| 8. fields 命名衝突 | InferredSchemaFieldDto で拡張 | DTO 継承で簡潔 |
| 9. 大ファイル境界 | (a) WinForms チャンク 1000 件逐次 POST | API 責務分離 |
| 10. 進捗とロールバック | (a)+(c) layer_import_job + finalize | 観測可能 + 再開可能 |
| 11. 起動権限チェック | (a)+(b) UI 非表示 + 開く瞬間再確認 | UX + 安全側 |
| 12. 既存 schema PUT | 残置 + 新 PATCH で統合 | 後方互換 |
| 13. MapInfo TAB | zip 必須 | Shapefile と一貫した UX |
| 14. CSV geom 列 UI | (a)+(c) 自動推測 + WKT 列対応 | 表計算ユーザの実利 |
| 15. fixture 配置 | windos-app.tests/Fixtures/import/ 直入れ | 100KB なら LFS 不要 |

## 次ステップ (Design Review 入口)

- 本案 (A) と、案 B (最小スコープ: Shapefile + GeoJSON のみ, 同期実装) / 案 C (段階導入: B 案 + 後フェーズで CSV/MIF/TAB) を並べ、Design Review で収束
- リスク高: B104 (fn_feature_bulk_insert) の性能と audit_log バッチ粒度 / B302 (GDAL での MIF/TAB 安定性) → Review で実証検証タスクを切るか判断
- B105 (layer_import_job) を採用するかは A 案の特徴。B 案では削って同期完結も検討可
