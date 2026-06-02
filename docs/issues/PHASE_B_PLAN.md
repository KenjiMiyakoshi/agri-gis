# agri-gis Phase B Plan — レイヤ編集 + レイヤインポート

Phase A (認証基盤) 完了を前提とした次フェーズの計画。スコープは「管理者がファイルから新規レイヤを作成し、属性スキーマを調整したうえで PostGIS に投入する」ところまで。WebGIS 側の表示は既存 `GET /api/layers` + `GET /api/features` のままで動くこと。

## スコープと前提

- 対応形式: Shapefile (zip), GeoJSON, CSV (Point 専用, lat/lng 列), MapInfo MIF/MID, MapInfo TAB
- 既存スターターレイヤ (サンプル圃場 / 観測点) は残し、`LayerAdminForm` から「新規追加」する形
- 自動スキーマ推論を WinForms 側で実行 → DataGridView で column name / type / required を調整 → 確定後に API に POST
- **GDAL は WinForms 同梱**: `MaxRev.Gdal.Core` + native DLL (`MaxRev.Gdal.WindowsRuntime.Minimal`) を NuGet で取得し、変換結果の GeoJSON / 属性表だけを API に送る (API/Docker 側は GDAL 非依存のまま)
- API は admin 専用エンドポイント群 (`/api/admin/layers*`) を新設。既存 `AdminOrgsEndpoints` / `AdminUsersEndpoints` と同じ書式 (RouteGroup + `RequireRole("admin")`)
- ロール: `admin` のみ `LayerAdminForm` を開ける。`general` / `guest` には `MainForm` のメニュー項目が出ない (Phase A の `ApplyGuestRestriction` と同じ要領)

## WBS (大項目 12 件)

| ID | 区分 | タイトル | 見積 | 主依存 |
|---|---|---|---|---|
| B1 | DB | `layers` 拡張 (description / srid_source / created_by / deleted_at) + 論理削除サポート | S | A106 |
| B2 | DB | `fn_layer_create` / `fn_layer_delete` (audit_log 連動、p_user_id/p_org_id 受け取り) | M | B1, A106 |
| B3 | DB | バルク投入用 `fn_feature_bulk_insert(JSONB 配列)` または `COPY 経路` の片方を実装 | M | B2 |
| B4 | API | `POST/GET/PATCH/DELETE /api/admin/layers` CRUD (AdminOrgs と同パターン) | M | B1, B2 |
| B5 | API | `POST /api/admin/layers/{id}/features:bulk` (GeoJSON FeatureCollection 受信、N 件単位でトランザクション) | M | B3, B4 |
| B6 | WinForms | `LayerAdminForm` 骨格 (admin チェック + MainForm からの起動 + 既存レイヤ一覧グリッド) | M | A402 系 |
| B7 | WinForms | ファイル取り込みウィザード Step1: 形式選択 + ファイル/zip 受け取り + GDAL で OGR DataSource を開く | L | B6 |
| B8 | WinForms | ウィザード Step2: スキーマ推論 (OGRFieldDefn → SchemaFieldDto) + 編集 UI (DataGridView, type/required) | M | B7 |
| B9 | WinForms | ウィザード Step3: SRID 確認 + 4326 への ST_Transform 相当 (GDAL) + プレビュー件数表示 + 投入実行 (B4+B5 呼び出し、進捗ダイアログ) | M | B7, B8, B4, B5 |
| B10 | Test (api.tests) | admin/general/guest の認可マトリクス + audit_log の actor_user_id / before-after_doc + 大量投入時のトランザクション境界 | M | B4, B5 |
| B11 | Test (windos-app.tests) | スキーマ推論ロジックの単体テスト (Shapefile / CSV / MIF サンプルを fixture 化) | M | B8 |
| B12 | Docs | `docs/layer-import.md` 新設 + `auth.md` のロールマトリクスに `/api/admin/layers*` を追記 + `PHASE_B_INDEX.md` | S | B4 完了後 |

合計目安: S×2 + M×8 + L×1 ≒ 約 14〜18 人日。

### Review② 未修復負債の合流判定 (Design で確定)
- H2 (`JsonSerializerOptions` 重複): B4/B5 で新規ファイル増えるので **B4 でまとめて `JsonOpts` 単一化** を推奨
- H4 (`AttributeEditorControl.ParentForm` キャスト): Phase B は AttributeEditor を触らない見込み → **Phase B では持ち越し**
- H5 (`MainForm` god class): `LayerAdminForm` の起動導線を MainForm に足すと更に肥大 → **B6 で `MainForm.Menu` 周りだけ別パーシャル / コマンドクラスに切り出し** を提案

## 設計論点 (Design 段階で 3 案検討)

1. **バルク insert 戦略**: (a) `POST /features:bulk` で受けて `fn_feature_insert` をループ vs (b) 専用 `fn_feature_bulk_insert(JSONB[])` vs (c) `COPY ... FROM STDIN` を Npgsql の `BeginBinaryImport` で。audit_log の粒度 (1 行 vs 1 バッチ) と性能トレードオフ。
2. **SRID 変換の責務**: (a) WinForms (GDAL `OGRCoordinateTransformation`) で 4326 化して送る vs (b) API 受信時に `ST_Transform` vs (c) DB 関数内で受信 SRID を渡して変換。CSV (lat/lng) は b/c が冗長。Shapefile/MIF は元 SRID 不明のケースあり。
3. **スキーマ推論の型優先順位**: OGR `OFTInteger/OFTInteger64/OFTReal/OFTString/OFTDate/OFTDateTime` → 内部 `integer/number/string/date` への写像。CSV は全列文字列なので 100 行サンプリングで `integer → number → boolean → date → string` の順に試行する案。
4. **.dbf の文字コード**: (a) GDAL の `SHAPE_ENCODING` を `CP932` 固定 vs (b) `.cpg` 同梱優先 + 無ければ UTF-8 試行 → 失敗で CP932 フォールバック vs (c) ユーザに Step1 で選ばせる。
5. **layer 論理削除と feature**: (a) `layers.deleted_at` だけ立てて feature は残す vs (b) feature_current を一括 history へ移送 vs (c) cascade 物理削除。バイテンポラル原則 (履歴保持) と整合させる必要。
6. **`layers.layer_type` の意味**: 既存定義は単一文字列 (例 `Polygon`)。Shapefile は単一型だが GeoJSON / MIF は `MultiPolygon` 混在あり。(a) 文字列を `MultiPolygon` で正規化保存 vs (b) `geometry_type` 列を新設して `layer_type` は用途ラベル (例 `field`) として分離 vs (c) PostGIS 側の制約 `geometry(MultiPolygon, 3857)` をレイヤごとに動的生成。
7. **新規レイヤの SRID 列の持ち方**: 現状は `feature_current.geom` が `geometry(Geometry, 3857)` 固定。インポート元 SRID は (a) `layers.source_srid` に記録だけ vs (b) feature 単位で `source_srid` を attribute に格納。
8. **スキーマ推論の "fields" 命名衝突**: 既存 `schema_json = {"fields":[{"name":..,"type":..,"required":..}]}`。`SchemaFieldDto` を再利用するか、import 用に `nullable` / `default` / `sample_values` を追加する別 DTO にするか。
9. **大ファイル投入の境界**: 1 万件超の Shapefile を想定するとき、(a) WinForms 側でチャンク (1000 件) して `POST /features:bulk` を逐次 vs (b) multipart で一気に送り API がストリーム読み vs (c) WinForms から `COPY` 用 TSV を生成して別エンドポイントへ。
10. **進捗とロールバック**: 途中失敗時に (a) レイヤ自体を rollback (作成自体を取り消す) vs (b) レイヤは残して投入済 feature だけ rollback vs (c) 半端な状態で残し再開可能なジョブ ID を返す。
11. **`LayerAdminForm` の起動権限チェック**: (a) MainForm 起動時に admin で無ければメニュー非表示 vs (b) 開く瞬間に再チェック + 401 時はログイン画面へ vs (c) サーバ側のみで `RequireRole("admin")` に委ねクライアントは寛容。
12. **既存 `PUT /api/admin/layers/{id}/schema` との関係**: docs/auth.md のロールマトリクスに既出。新 `PATCH /api/admin/layers/{id}` と統合するか、schema_json 専用エンドポイントを残し続けるか。
13. **MapInfo TAB の取り扱い**: TAB は複数ファイル構成 (.tab/.dat/.map/.id/.ind)。zip 受け取り必須にするか、フォルダ選択を許すか。GDAL `MITAB` ドライバの安定性確認も含む。
14. **CSV の geom 列指定 UI**: (a) ヘッダから `lat/latitude/y` を自動推測 vs (b) Step1 で必ずユーザに 2 列選ばせる vs (c) WKT 列もサポート。
15. **テスト用 fixture 配置**: 各形式の最小サンプル (Shapefile zip / GeoJSON / CSV / MIF/MID / TAB) を `windos-app.tests/Fixtures/import/` に置くか、`db/fixtures/` に統合するか。バイナリの git LFS 要否。

## 読むべきファイル (Design/Review エージェント向け)

### 認証/認可・既存パターン
- `docs/auth.md` — ロールマトリクスと環境変数。新 `/api/admin/layers*` を追記する基準
- `api/Endpoints/AdminOrgsEndpoints.cs` — admin CRUD の最小骨格 (B4 のコピー元)
- `api/Endpoints/AdminUsersEndpoints.cs` — パスワード変更を別経路にした事例 (schema_json 分離の参考)
- `api/Endpoints/AdminEndpoints.cs` — 既存 `PUT /api/admin/layers/{id}/schema` の位置確認
- `api/Endpoints/RequestContext.cs` — `ICurrentUser` 経由の actor 取り出しパターン

### レイヤ / フィーチャ DB
- `db/init/001_init.sql` — `layers` / `feature_current` の現行 DDL (geom は 3857 固定)
- `db/migration/001_layers_add_schema.sql` — `schema_json` / `schema_version`
- `db/migration/005_layer_schema_version.sql` — append-only スキーマ履歴
- `db/migration/009_fn_layer_schema_upsert.sql` — schema upsert + audit_log の書式 (B2 で踏襲)
- `db/migration/0A06_fn_args_extension.sql` — `p_user_id UUID, p_org_id INT` の引数規約 (B2/B3 の関数も同じ末尾に置く)
- `db/migration/006_fn_feature_insert.sql` — feature insert の audit 出力形 (B3 のループ案 / バルク案で差し替える基準)

### WinForms 既存基盤
- `windos-app/Forms/MainForm.cs` — `LayerAdminForm` 起動導線、ApplyGuestRestriction の踏襲ポイント
- `windos-app/Forms/LoginForm.cs` — Modal フォーム + DI パターン
- `windos-app/Forms/AttributeEditorControl.cs` — schema_json から DataGridView を作る既存実装 (B8 のスキーマ編集 UI で再利用検討)
- `windos-app/Auth/ISessionStore.cs` / `Services/BearerHandler.cs` — admin role 判定 / API 呼び出し経路
- `windos-app/Services/ApiClient.cs` — 新メソッド (CreateLayer / BulkInsertFeatures) を生やす場所

### Review② 負債参照
- `windos-app/Forms/AttributeEditorControl.cs` (H4) / `windos-app/Forms/MainForm.cs` (H5) / `api/Json/JsonOpts.cs` 想定 (H2)

## 次ステップ

Design 段階では上記 15 論点について 3 案を起こし、案 P/Q/R として収束させる。WBS のうち B7 (L 見積) と B9 (M だが GDAL 連携) はリスクが高く、Design で UI モック + GDAL 呼び出しシーケンスをスケッチしておくこと。
