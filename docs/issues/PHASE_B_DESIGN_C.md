# Phase B Design 案 C — 既存踏襲 (Conservative Extension)

Plan 文書 `PHASE_B_PLAN.md` の 15 論点に対する 1 つ目の収束案。テーマは **「Phase A で確立した admin CRUD パターン / バイテンポラル関数規約 / WinForms フォーム構造を素直に横展開する」** こと。新しい抽象や最適化は意図的に避け、レビュー差分を最小化する。

## 設計コンセプト

- **AdminOrgsEndpoints の写経**: `LayerAdminEndpoints` は `RouteGroupBuilder` ベースで GET/POST/PATCH/DELETE を 4 本生やすだけ。`RequireRole("admin")` も同じ位置で適用
- **新規 DB 関数を最小化**: バルク insert は `fn_feature_bulk_insert` を新設せず、**API 側 Tx 内で `fn_feature_insert` を N 回呼ぶ**。`fn_layer_create` / `fn_layer_delete` も Phase A の `fn_layer_schema_upsert` と同じ引数規約 (`p_user_id UUID, p_org_id INT` 末尾) で揃える
- **GDAL は WinForms 同梱を貫徹**: API/Docker は GDAL 非依存。SRID 変換 / 文字コード判定 / 型推論はすべて WinForms 内で完結し、API には UTF-8 GeoJSON + 確定済みスキーマ JSON だけ届く
- **WinForms フォームは Designer.cs パターン**: `LoginForm` / `MainForm` と同じく partial class + TableLayoutPanel。ウィザードは TabControl の Page 切替で簡易表現 (新規 `WizardFramework` クラスは導入しない)
- **スキーマ推論は保守的**: 迷ったら `string`。型確定後の修正は UI で人間に委ねる (誤推論で投入失敗するより手戻り少)
- **Review② 負債 (H2/H4/H5) は触らない**: Phase B 完了後の独立サイクルへ送る。`LayerAdminForm` の起動導線も MainForm にメニュー 1 項目追加で済ませ、god class 問題は次へ持ち越し

## 設計論点への回答 (15 件)

### 1. バルク insert 戦略 → **(a) ループ案を採用**
- `POST /api/admin/layers/{id}/features:bulk` 受信 → API 内で `await using var tx = await conn.BeginTransactionAsync()` → `foreach` で `SELECT fn_feature_insert(...)` を回す → `COMMIT`
- audit_log は 1 feature あたり 1 行 (`fn_feature_insert` 内で既に書かれている)。Phase A で確立した粒度をそのまま継承
- 性能リスク: 1 万件で 30 秒〜1 分が見込み。Phase B の許容範囲とする (1 万件を超えるレイヤは Phase B スコープ外と Docs に明記)
- 採用理由: 新 DB 関数 0 個 / トランザクション境界が明示的 / audit_log 整形が既存と完全一致

### 2. SRID 変換の責務 → **(a) WinForms 側で 4326 化**
- WinForms 取り込み時に `OGRCoordinateTransformation` で 4326 へ変換 → GeoJSON は常に 4326 で API に渡す
- API/DB は `ST_SetSRID(ST_GeomFromGeoJSON(...), 4326)` → `ST_Transform(geom, 3857)` の固定 2 ステップで feature_current.geom に保存
- CSV (lat/lng) は元から 4326 とみなしクライアントで変換不要 (パススルー)
- 採用理由: DB 関数の引数を増やさない / 既存 `fn_feature_insert(p_geom geometry)` のシグネチャを変えない

### 3. スキーマ推論の型優先順位 → **OGR 型をそのまま写像 + CSV は保守側**
- Shapefile/MIF/TAB/GeoJSON: `OFTInteger/OFTInteger64 → integer`, `OFTReal → number`, `OFTDate/OFTDateTime → string` (Phase B は `date` 型を schema に出さない方針), それ以外 → `string`
- CSV: 全列を一旦 `string` で推論し、ヘッダ名が `id` / `*_id` / `lat` / `lng` だけ特殊扱い。型変更は WinForms の DataGridView で手動
- 採用理由: 既存 `schema_json.fields[].type` が `string/integer/number/boolean` の 4 種限定 (Phase A 確認済) のため写像表が単純

### 4. .dbf 文字コード → **(a) `SHAPE_ENCODING=CP932` 固定 + UI で警告**
- 国内圃場データ前提では CP932 が大多数。Step1 でファイル取込時に `SHAPE_ENCODING` 環境変数を CP932 で `Environment.SetEnvironmentVariable` 設定
- `.cpg` が同梱されていれば GDAL 側が優先する (デフォルト挙動)
- Step2 のプレビューに「文字化けがある場合は Step1 に戻り CP932 以外を選択してください」と注記。実際の選択 UI は Phase B では実装せず、Docs と Issue にだけ残す
- 採用理由: 95% ケースを 1 行で吸収、残り 5% は Phase C の Backlog

### 5. layer 論理削除と feature → **(a) `layers.deleted_at` だけ立てて feature_current は据え置き**
- `DELETE /api/admin/layers/{id}` は `layers.deleted_at = now()` を打つだけ
- `GET /api/layers` 系は既に `WHERE deleted_at IS NULL` フィルタ済 (`AdminOrgsEndpoints` の DELETE パターン参照)
- feature_current の物理削除は別ジョブ。バイテンポラル原則上 feature の履歴は残るので一貫
- 採用理由: 復活操作 (誤削除の取消) を将来追加しやすい

### 6. `layers.layer_type` の意味 → **(a) 文字列を `MultiPolygon` で正規化**
- 既存定義の単一文字列カラムを維持。GeoJSON / MIF で Polygon / MultiPolygon が混在する場合は **MultiPolygon に寄せて保存** (`ST_Multi` 相当を WinForms 側で適用)
- LineString も同様に MultiLineString に正規化
- 採用理由: PostGIS の型制約と feature_current.geom の `geometry(Geometry, 3857)` 汎用型のままで運用継続。スキーマ拡張ゼロ

### 7. 新規レイヤの SRID 列 → **(a) `layers.source_srid` に記録のみ (新規列追加)**
- `B1` で `layers` に `source_srid INT NULL` を追加。表示・参照用途のみ。feature 側へは複製しない
- WinForms のレイヤ一覧で「元 SRID」列として表示する程度
- 採用理由: feature attribute を汚さない / 既存 `GET /api/features` の JSON 形を変えない

### 8. SchemaFieldDto の使い回し → **既存 DTO をそのまま再利用**
- `SchemaFieldDto(Name, Type, Required)` をインポート UI でも使う
- `nullable` / `default` / `sample_values` は **Phase B では追加しない**。サンプル値プレビューは DataGridView の別領域で表示するが DTO 化はしない
- 採用理由: Phase A の `fn_layer_schema_upsert(p_schema_json JSONB)` の受け取り形が既存 DTO と一致しているため、API 側の変更不要

### 9. 大ファイル投入の境界 → **(a) WinForms 側で 1000 件チャンク + 逐次 POST**
- WinForms が GeoJSON FeatureCollection を 1000 件ずつ分割 → `POST /admin/layers/{id}/features:bulk` を直列に呼ぶ
- API 側はチャンク 1 回 = 1 Tx (論点 1 と整合)
- 進捗ダイアログには「N / Total 件投入済」を表示
- 採用理由: HTTP リクエストサイズが予測可能 / ストリーミング受信の複雑性回避 / multipart 不要

### 10. 進捗とロールバック → **(c) 半端で残し再開可能 (ただし単純実装)**
- チャンク途中失敗時はレイヤと投入済 feature は残す。WinForms 側でエラーダイアログ「N 件まで投入済。残りを再投入しますか?」
- ジョブ ID は導入せず、再投入はユーザが手動で残行を切り出し再 POST する運用 (Phase B では UI 自動化しない)
- 「やり直し」したい場合は `DELETE /api/admin/layers/{id}` で論理削除 → 再作成
- 採用理由: ジョブテーブルの新設を回避 / Phase B の WBS を膨らませない

### 11. `LayerAdminForm` の起動権限チェック → **(a) MainForm でメニュー非表示 + (c) サーバ側 RequireRole**
- クライアント: `MainForm.OnLoad` で `_session.Role != "admin"` ならメニュー項目 `LayerAdmin` を `Visible = false`。Phase A の `ApplyGuestRestriction` と同型
- サーバ: 全 `/api/admin/layers*` は `RequireRole("admin")` で 403 を返す (Phase A 実装そのまま)
- 開く瞬間の再チェックは省略 (セッション中の role 変更ケースは Phase B では非考慮)
- 採用理由: クライアント実装が 5 行で済む

### 12. 既存 `PUT /api/admin/layers/{id}/schema` との関係 → **残置 + PATCH と棲み分け**
- `PATCH /api/admin/layers/{id}` は `name` / `description` / `layer_type` などレイヤメタを更新
- 既存 `PUT /api/admin/layers/{id}/schema` は `schema_json` 専用 (Phase A から動いている / 直接 `fn_layer_schema_upsert` を叩く) のため、**触らず温存**
- Docs (`auth.md`) に「PATCH はメタのみ、schema は PUT」と一行注記
- 採用理由: Phase A 完成済 API の挙動を一切変えない / 既存テスト (`A506-test-admin-crud.md`) が無傷

### 13. MapInfo TAB の取り扱い → **zip 必須 + GDAL MITAB に依存**
- ファイル選択ダイアログは `.zip` のみ受理。中身に `.tab + .dat + .map + .id` が揃っているかを zip 展開直後にチェック
- フォルダ選択は Phase B では非対応 (運用上 zip 化を促す)
- GDAL `MITAB` ドライバの既知不具合 (CP932 系) は CP932 固定方針 (論点 4) と整合
- 採用理由: ファイル参照の単一窓口化 / 5 形式の入口を「zip / 単一ファイル」の 2 種に揃え UI を簡素化

### 14. CSV の geom 列指定 UI → **(a) ヘッダ自動推測 + Step1 で確認**
- `lat` / `latitude` / `y` を緯度、`lng` / `lon` / `longitude` / `x` を経度と推測 (大小文字無視)
- Step1 のプレビューに「緯度列: lat / 経度列: lng」と表示し、コンボボックスで列を差替可能 (デフォルト値だけ自動推測)
- WKT 列サポートは Phase B 対象外 (Phase C で `geom_wkt` 列名による拡張を検討)
- 採用理由: 国内 CSV の慣習にマッチ / 自動化と UI 確認の両立

### 15. テスト fixture 配置 → **`windos-app.tests/Fixtures/import/` に集約 + git LFS 不要**
- 5 形式の最小サンプル (各 < 10 KB) を `windos-app.tests/Fixtures/import/` 配下に置く
  - `shape_minimal.zip` / `geojson_minimal.json` / `csv_minimal.csv` / `mif_mid_minimal.zip` / `tab_minimal.zip`
- API テスト (`api.tests`) はそこから読み出すかコピー (依存パスは `TestPaths` ヘルパで吸収)
- バイナリは合計 50 KB 程度なので git LFS は不要。`.gitattributes` 設定もしない
- 採用理由: テスト fixture の所在を 1 か所に集約 / B11 で WinForms 側が主担当のため自然

## 採用案によるアーキテクチャ図

```
[WinForms LayerAdminForm]
  ├─ Step1: ファイル選択 + zip 展開 + GDAL OGR Open + SHAPE_ENCODING=CP932
  ├─ Step2: OGRFieldDefn → SchemaFieldDto[] 推論 + DataGridView 編集
  ├─ Step3: SRID 確認 + OGRCoordinateTransformation → 4326 GeoJSON 生成
  └─ Step4: 投入実行
        ├─ POST /api/admin/layers (name/description/layer_type/source_srid/schema_json)
        └─ POST /api/admin/layers/{id}/features:bulk × N回 (1000件チャンク)
              └─ [API LayerAdminEndpoints]
                    └─ Tx { foreach feat: SELECT fn_feature_insert(...) } COMMIT
                          └─ [DB] feature_current INSERT + audit_log INSERT (既存)
```

## ファイル別の差分概要

### DB (db/migration/)
- `010_layers_extend.sql` (B1): `layers` に `description TEXT`, `source_srid INT`, `created_by UUID`, `deleted_at TIMESTAMPTZ` を追加
- `011_fn_layer_create.sql` (B2): `fn_layer_create(p_name, p_layer_type, p_schema_json, ..., p_user_id, p_org_id)` 内部で `INSERT layers RETURNING id` + audit_log
- `012_fn_layer_delete.sql` (B2): `fn_layer_delete(p_layer_id, p_user_id, p_org_id)` で `UPDATE layers SET deleted_at = now()` + audit_log

### API (api/Endpoints/)
- `LayerAdminEndpoints.cs` (B4, 新規): `AdminOrgsEndpoints.cs` をコピーして `organizations` → `layers` に置換 + `fn_layer_create` / `fn_layer_delete` 呼び出し
- `LayerAdminEndpoints.cs` 末尾に `/features:bulk` ハンドラ追加 (B5): Tx でループ insert
- `Program.cs` の `MapGroup("/api/admin/layers").RequireRole("admin").MapLayerAdminEndpoints()` 行を追加

### WinForms (windos-app/)
- `Forms/LayerAdminForm.cs` + `.Designer.cs` (B6, 新規): TabControl で Step1〜4
- `Services/GdalImporter.cs` (B7-9, 新規): OGR 呼び出しラッパ (Open / 列挙 / SRID 変換 / GeoJSON 化)
- `Services/SchemaInference.cs` (B8, 新規): OGRFieldDefn → SchemaFieldDto[]
- `Services/ApiClient.cs` (既存に追記): `CreateLayerAsync` / `BulkInsertFeaturesAsync`
- `Forms/MainForm.cs` (既存に 1 行): メニュー項目 + Click ハンドラ (admin 限定 Visible)
- `windos-app.csproj`: `MaxRev.Gdal.Core` + `MaxRev.Gdal.WindowsRuntime.Minimal` 追加

### Tests
- `api.tests/LayerAdminEndpointsTests.cs` (B10): 認可マトリクス + audit_log 検証 + bulk Tx 境界
- `windos-app.tests/Services/SchemaInferenceTests.cs` (B11): 5 形式 fixture からの推論結果検証

### Docs
- `docs/layer-import.md` 新設 (B12)
- `docs/auth.md` ロールマトリクスに `/api/admin/layers*` 追記

## トレードオフと諦めた点

| 諦めた点 | 理由 |
|---|---|
| バルク insert の高速化 (COPY / 専用関数) | 1 万件 30〜60 秒は許容 / 新規 DB 関数追加コストが Phase B のスケジュールリスク |
| ジョブ管理テーブル | テーブル新設 + 状態機械の設計が WBS を膨らませる |
| 文字コード選択 UI | CP932 固定で 95% 吸収 / 残り 5% は Docs 案内 |
| H2/H4/H5 の同時修復 | レビュー差分の集中で B4/B6 のリスクが上がる |
| WKT CSV サポート | Phase C で需要が出てから |

## Review/Issue 化時のチェックポイント

- `fn_layer_create` / `fn_layer_delete` が `A106` の `p_user_id, p_org_id` 末尾規約に合っているか
- `LayerAdminEndpoints` が `AdminOrgsEndpoints` の **コピーから機械的派生** になっており独自パターンを混ぜていないか
- bulk insert ハンドラの Tx スコープが `await using` ベースで例外時 rollback されているか
- `LayerAdminForm` の admin チェックが `MainForm.OnLoad` の 1 箇所に閉じているか (god class を更に大きくしないこと)
- GDAL native DLL が `windos-app/bin/` 配下に展開され API/Docker 側に漏れていないか

## 次の Design への申し送り

- 案 D (積極的最適化) / 案 E (将来拡張重視) との比較時は **「Phase B 完了までの実装日数 14〜18 人日」** を基準線とする
- 案 C は Plan 見積の下限値に近く、論点 1/9 で性能を犠牲にしている → 1 万件超のレイヤを Phase B 内で扱う必要が出たら採用見直し
- 案 C は 「Phase A で動いているもの全てに極力触らない」 ことを優先しているため、Phase A の負債 (H2/H4/H5) は Phase B 内で改善されず Phase C 以降に持ち越される
