# A105: C2 修復 - 4 関数の to_jsonb から geom を抜き geom_geojson を追加

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 1d |
| Depends on | A103 |
| Blocks | A507 |

## 概要
`fn_feature_insert` / `fn_feature_update` / `fn_feature_delete` および audit 出力用のヘルパで、`to_jsonb(fc.*)` の結果から `geom` (geometry binary) を抜き、代わりに `geom_geojson` (TEXT) を含める (C2 修復)。

## 背景・目的
採択案「案 P」の PL/pgSQL セクション:
> **C2 修復**: 4 関数の `to_jsonb(fc.*)` から `geom` を抜き `geom_geojson` 追加。NULL geom 時は NULL を JSONB に

現状 `to_jsonb(feature_current.*)` は geometry を WKB hex として `\"geom\": \"0101...\"` の形で audit_log に保存しており、可読性も検索性もない。GeoJSON 文字列化することで diff 検証が可能になる。

## スコープ
### 含む
- 4 関数の audit 出力部を `before_doc` / `after_doc` 計算前に geom を strip + `geom_geojson` 注入する共通パターンへ
- 対象関数: `fn_feature_insert`, `fn_feature_update`, `fn_feature_delete`, （必要なら）`fn_layer_schema_upsert` 等の before/after 出力を持つ関数
- NULL geom の場合は `geom_geojson = NULL`、JSONB の `null` を入れる
- `db/migration/0A05_fn_audit_geom_strip.sql`

### 含まない
- API 層での GeoJSON 整形 (不要、audit_log は DB 出力)
- C1 修復 (A104) との混在は避ける（ファイル分離）

## 受け入れ条件 (Acceptance Criteria)
- [ ] `audit_log.before_doc` / `after_doc` に `\"geom\"` key が存在しない
- [ ] `audit_log.before_doc->>'geom_geojson'` が有効な GeoJSON 文字列（または NULL）
- [ ] geom = NULL の feature について `geom_geojson IS NULL`
- [ ] `ST_AsGeoJSON(ST_Transform(geom, 4326))` 相当の WGS84 GeoJSON が出る
- [ ] 既存 0303/0304 テストが green

## 影響ファイル
- `D:\proj\agri-gis\db\migration\0A05_fn_audit_geom_strip.sql` (新規)
- 既存 `db/migration/006_fn_feature_insert.sql`, `007_fn_feature_update.sql`, `008_fn_feature_delete.sql`, `009_fn_layer_schema_upsert.sql` の関数を CREATE OR REPLACE で上書き

## 実装ノート
共通の式パターン:

```sql
-- before/after 計算（geom strip + geom_geojson 注入）
SELECT to_jsonb(fc.*) - 'geom'
       || jsonb_build_object(
              'geom_geojson',
              CASE
                  WHEN fc.geom IS NULL THEN NULL::jsonb
                  ELSE to_jsonb(ST_AsGeoJSON(ST_Transform(fc.geom, 4326))::text)
              END
          )
INTO v_after
FROM feature_current fc
WHERE entity_id = p_entity_id;
```

または PL/pgSQL ヘルパ関数 `fn_feature_row_to_audit_jsonb(p_entity_id UUID)` を作って 4 関数から呼び出す形でも OK（DRY）。

注意点:
- A104 (C1) と同じ関数 `fn_feature_update` を触るので、マージ順序は A104 → A105 の順
- `ST_AsGeoJSON` の数値精度はデフォルト 9 桁、必要なら第 2 引数で調整

## テスト観点
- A507 (AuditLogGeomStripTests):
  - INSERT/UPDATE/DELETE 後の `audit_log.after_doc` に `geom` key が無い
  - `after_doc->>'geom_geojson'` が `{"type":"Polygon",...}` 形式
  - geom = NULL のテストケースで `geom_geojson IS NULL`
