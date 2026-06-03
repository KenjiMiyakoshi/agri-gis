# Phase E Design 案 A — L-1 + S-1 (Phase A 流儀完全踏襲、採択ベース)

`PHASE_E_PLAN.md` §3.1 の L-1 + S-1 組合せ。`PHASE_E_DESIGN_P.md` の採択ベース。

## 1. アーキテクチャ (Phase A `feature_*` の双子)

```
┌──────────────────────┐        ┌──────────────────────┐
│   feature_current     │   →   │   feature_history     │  (Phase A)
│   (valid_from, _to,   │  退避  │   (同上)               │
│    deleted_at)        │       └──────────────────────┘
└──────────────────────┘
         ↑ FK layer_id
┌──────────────────────┐        ┌──────────────────────┐
│   layers              │   →   │   layer_history       │  ← Phase E 新設 (案 A)
│   + valid_from/_to    │  退避  │   (同上 + version)    │
│   + deleted_at (互換) │       └──────────────────────┘
└──────────────────────┘
         ↑ FK layer_id (1対多)
┌──────────────────────┐        ┌──────────────────────┐
│  layer_schema_version │       │  layer_style_version │  ← Phase E 新設 (案 A)
│  (Phase A 0106)       │       │  (Phase A 形式踏襲)   │
└──────────────────────┘        └──────────────────────┘
```

## 2. DB スキーマ詳細

### 2.1 `layer_history` (新設)

```sql
CREATE TABLE IF NOT EXISTS layer_history (
    history_id      BIGSERIAL    PRIMARY KEY,
    layer_id        INTEGER      NOT NULL,           -- layers.layer_id への論理 FK (CASCADE しない、history は永続)
    layer_name      TEXT         NOT NULL,
    layer_type      TEXT         NOT NULL,
    geometry_type   TEXT         NULL,
    description     TEXT         NULL,
    source_format   TEXT         NULL,
    source_srid     INTEGER      NULL,
    schema_version  INTEGER      NOT NULL,
    schema_json     JSONB        NOT NULL,
    style_json      JSONB        NOT NULL DEFAULT '{}'::jsonb,
    owner_org_id    INTEGER      NULL,
    is_shared       BOOLEAN      NOT NULL,
    created_by      UUID         NULL,
    created_org_id  INTEGER      NULL,
    version         INTEGER      NOT NULL,           -- 楽観ロック値
    valid_from      DATE         NOT NULL,
    valid_to        DATE         NOT NULL,
    created_at      TIMESTAMPTZ  NOT NULL,
    updated_at      TIMESTAMPTZ  NOT NULL,
    archived_at     TIMESTAMPTZ  NOT NULL DEFAULT now()  -- history に退避した時刻
);
CREATE INDEX ix_layer_history_layer_id_valid ON layer_history(layer_id, valid_from, valid_to);
```

### 2.2 `layers` 拡張 (DDL)

```sql
ALTER TABLE layers
    ADD COLUMN IF NOT EXISTS valid_from DATE NOT NULL DEFAULT CURRENT_DATE,
    ADD COLUMN IF NOT EXISTS valid_to   DATE NOT NULL DEFAULT '9999-12-31'::date,
    ADD COLUMN IF NOT EXISTS version    INTEGER NOT NULL DEFAULT 1;

-- 既存 deleted_at IS NOT NULL の行は valid_to = deleted_at::date に backfill
UPDATE layers SET valid_to = deleted_at::date WHERE deleted_at IS NOT NULL;
```

### 2.3 `layer_style_version` (新設、`layer_schema_version` のコピー)

```sql
CREATE TABLE IF NOT EXISTS layer_style_version (
    layer_id       INTEGER      NOT NULL REFERENCES layers(layer_id) ON DELETE RESTRICT,
    style_version  INTEGER      NOT NULL,
    style_json     JSONB        NOT NULL,
    valid_from     DATE         NOT NULL DEFAULT CURRENT_DATE,
    valid_to       DATE         NOT NULL DEFAULT '9999-12-31'::date,
    created_by     UUID         NULL REFERENCES users(user_id) ON DELETE SET NULL,
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    PRIMARY KEY (layer_id, style_version)
);
CREATE INDEX ix_layer_style_version_active
    ON layer_style_version(layer_id, valid_from, valid_to);
```

### 2.4 `feature_asof` VIEW (新設)

```sql
CREATE OR REPLACE VIEW feature_asof AS
SELECT feature_id, layer_id, entity_id, version, valid_from, valid_to,
       attributes_schema_version, created_by, updated_by, created_at, updated_at,
       attributes, geom
  FROM feature_current
UNION ALL
SELECT feature_id, layer_id, entity_id, version, valid_from, valid_to,
       attributes_schema_version, created_by, updated_by, created_at, updated_at,
       attributes, geom
  FROM feature_history;
```

GeoServer は `feature_asof` を SQL view ではなく **テーブル/View** featureType として publish (parametric view 不採用、CQL_FILTER で `valid_from <= asOf AND asOf < valid_to` を後付けする方が素直)。

## 3. 関数 (Phase A 流儀踏襲)

### 3.1 `fn_layer_update` (新設、WE1)

シグネチャ:
```sql
CREATE OR REPLACE FUNCTION fn_layer_update(
    p_layer_id        INTEGER,
    p_layer_name      TEXT,
    p_layer_type      TEXT,
    p_geometry_type   TEXT,
    p_description     TEXT,
    p_source_format   TEXT,
    p_source_srid     INTEGER,
    p_expected_version INTEGER,
    p_actor           TEXT,
    p_request_id      UUID,
    p_user_id         UUID,
    p_org_id          INTEGER
) RETURNS TABLE(layer_id INTEGER, version INTEGER);
```

挙動 (Phase A C1 ロジック踏襲):
1. `SELECT ... FROM layers WHERE layer_id=p_layer_id AND valid_to='9999-12-31' AND version=p_expected_version FOR UPDATE` — 楽観ロック
2. 旧行を `layer_history` に INSERT (`valid_to=CURRENT_DATE`, `archived_at=now()`)
3. `layers` を UPDATE (新値 + `valid_from=CURRENT_DATE` + `version+1` + `updated_at=now()`)
4. `audit_log` に INSERT (`actor`, `actor_user_id`, `before_doc`, `after_doc`, `meta_jsonb={layer_id, version_before, version_after}`)

### 3.2 `fn_layer_delete` v2 (`fn_layer_delete` を CREATE OR REPLACE、WE1)

旧挙動: `UPDATE layers SET deleted_at=now()`
新挙動:
1. 旧行を `layer_history` に退避 (`valid_to=CURRENT_DATE`)
2. `layers` を UPDATE (`deleted_at=now()`, `valid_to=CURRENT_DATE`) — `deleted_at` 二重書きは Phase E' まで継続
3. `audit_log` INSERT

### 3.3 `fn_layer_style_upsert` (新設、`fn_layer_schema_upsert` の双子、WE1)

```sql
CREATE OR REPLACE FUNCTION fn_layer_style_upsert(
    p_layer_id     INTEGER,
    p_style_json   JSONB,
    p_actor        TEXT,
    p_request_id   UUID,
    p_user_id      UUID,
    p_org_id       INTEGER
) RETURNS TABLE(layer_id INTEGER, style_version INTEGER);
```

挙動:
1. 現行 `layer_style_version` の最大 version + 1 を新 version とする
2. 旧 active 行を `UPDATE layer_style_version SET valid_to=CURRENT_DATE WHERE layer_id=p_layer_id AND valid_to='9999-12-31'`
3. 新行 INSERT (`style_version=new_v`, `style_json=p_style_json`, `valid_from=CURRENT_DATE`, `valid_to='9999-12-31'`)
4. `layers.style_json` も同期更新 (current value を冗長保持、SELECT の高速化のため)
5. `audit_log` INSERT

## 4. API endpoint (Phase E で追加/変更)

### 4.1 共通: `AsOfParser`

`api/Shared/AsOfParser.cs` 新規。既存 `FeatureEndpoints.ParseAsOf` を移植。

```csharp
public static class AsOfParser
{
    public static DateOnly? TryParse(string? asOf) { ... }
    // 戻り値: null = 現在、それ以外 = 指定日。ISO datetime は 422 ValidationException
}
```

### 4.2 `GET /api/layers?asOf=YYYY-MM-DD` (拡張)

```sql
-- asOf 無し
SELECT ... FROM layers WHERE valid_to = '9999-12-31'::date

-- asOf あり
SELECT ... FROM layers WHERE valid_from <= @asof AND @asof < valid_to
UNION ALL
SELECT ... FROM layer_history WHERE valid_from <= @asof AND @asof < valid_to
```

### 4.3 `GET /api/admin/layers?asOf=` (拡張)

同上 + `includeDeleted=true` と排他 (asOf 指定時は includeDeleted は無視)。

### 4.4 `GET /api/admin/layers/{id}/style?asOf=` (拡張)

```sql
-- asOf 無し
SELECT style_json FROM layers WHERE layer_id=@id

-- asOf あり
SELECT style_json FROM layer_style_version
 WHERE layer_id=@id AND valid_from <= @asof AND @asof < valid_to
```

### 4.5 `PUT /api/admin/layers/{id}/style` (変更)

旧: `UPDATE layers SET style_json=...` + GeoServer 同期
新: `fn_layer_style_upsert(...)` 呼び出し (Tx 内) → GeoServer 同期 (`IGeoServerStyleSync`) → 失敗時 Tx rollback (Phase D の挙動継続)

### 4.6 `GET /tiles/{layerId}/{theme}/{z}/{x}/{y}.png?asOf=YYYY-MM-DD` (拡張)

asOf 無し: 既存通り `agrigis:feature_current` featureType + `CQL_FILTER=layer_id={N}` + `Cache-Control: max-age=3600`
asOf あり: `agrigis:feature_asof` featureType + `CQL_FILTER=layer_id={N} AND valid_from <= '{asOf}' AND '{asOf}' < valid_to` + `Cache-Control: no-store`

### 4.7 `GET /api/layers/{id}/extent?asOf=` / `GET /api/layers/{id}/at?asOf=` (拡張)

`feature_asof` view + valid_from/valid_to 絞り込み。

## 5. GeoServer 経路

### 5.1 featureType 追加

`tools/geoserver-setup/setup.ps1` を WE3 で拡張:
1. 既存: workspace `agrigis` + datastore `postgis_main` + featureType `feature_current` + style `t_default`
2. WE3 追加: featureType `feature_asof` (同 datastore、source=PostgreSQL VIEW `feature_asof`)

setup.ps1 は idempotent (409 既存 → 続行)。dev 環境では再実行で featureType 追加。

### 5.2 cache 戦略

| 経路 | Cache-Control |
|------|---------------|
| `/tiles/.../{z}/{x}/{y}.png` (asOf 無し) | `max-age=3600, public` (既存維持) |
| `/tiles/...?asOf=YYYY-MM-DD` | `no-store` (asOf hits は頻度低、cache 肥大化防止) |
| `/tiles/selection/{sid}/...` | `no-store` (Phase D 既存) |

## 6. WinForms 変更

| ファイル | 変更概要 |
|----------|---------|
| `windos-app/Forms/MainForm.cs` | ツールバーに `DateTimePicker asOfPicker` 追加。値変更で `apiClient.GetLayersAsync(asOf)` 再ロード + bridge `layer_select.asOf` 再送 |
| `windos-app/Forms/MainForm.cs` | `asOf != null` のとき編集ボタン (`POST/PATCH/DELETE` 関連) を `Enabled = false` |
| `windos-app/Services/ApiClient.cs` | `GetLayersAsync` / `GetLayerStyleAsync` / `GetLayersExtentAsync` 等 4 メソッドに `DateOnly? asOf` 引数追加 |
| `windos-app/Controls/AttributeEditorControl.cs` | `LoadFeature` 時に asOf を保持、`saveButton.Enabled = (asOf == null)` |
| `windos-app.tests` | `MainFormAsOfPickerTests` (DateTimePicker 値変更 → asOf 配線確認) |

## 7. WebGIS 変更

| ファイル | 変更概要 |
|----------|---------|
| `webgis/src/controllers/layer.ts` | `setBaseLayerSource(ctx, layerId, theme, asOf?)` で URL に `?asOf=` 追加 |
| `webgis/src/main.ts` | `layer_select` envelope の `p.asOf` を受領して loadFeatures に渡す |
| `webgis/src/bridge/messages.ts` | (既存) `LayerSelectPayload.asOf?: string` は定義済、配線のみ |
| `webgis/src/api/client.ts` | `getLayerExtent(layerId, asOf?)` / `getFeaturesAt(layerId, x, y, tolerance?, asOf?)` 引数追加 |

## 8. テスト戦略

### 8.1 新規テスト

| カテゴリ | テスト |
|----------|-------|
| API | `LayerAsOfTests` (現在 / 過去時点で layer 一覧が変わる) |
| API | `StyleHistoryTests` (PUT × 3 で style_version=1/2/3、asOf で過去 SLD 取得) |
| API | `TilesAsOfTests` (asOf あり → feature_asof featureType に切替、no-store ヘッダ) |
| API | `AsOfParserTests` (`/Shared/AsOfParser.cs` の単体テスト) |
| API | `LayerUpdateBitemporalTests` (PATCH × 2 で version=1→2、layer_history に 1 行退避) |
| WinForms | `MainFormAsOfPickerTests` (asOfPicker 値変更 → bridge envelope + 編集ボタン disable) |
| WebGIS | `tileLayerAsOf.test.ts` (URL に `?asOf=` 含む) |

### 8.2 e2e smoke

- 2025-01-01 時点で layer 7 を作成 → 2025-03-01 で削除
- `GET /api/layers?asOf=2025-02-15` → layer 7 が返る
- `GET /api/layers?asOf=2025-04-15` → layer 7 が返らない
- 同じ流れで style_version も検証

## 9. 案 A 採用根拠 まとめ

- Phase A C1/C2 修復で確立した半開区間ロジックを 100% 再利用 (再発明ゼロ)
- 既存 FK 構造を破壊しない (`feature_current.layer_id → layers.layer_id` は引き続き UNIQUE)
- `layer_schema_version` と `layer_style_version` が完全対称、API 命名も対称 (`fn_layer_schema_upsert` ↔ `fn_layer_style_upsert`)
- GeoServer は `feature_asof` view を追加するだけ、既存タイル経路は無変更

## 10. 採用度

`PHASE_E_DESIGN_P.md` で **採択**。本ドキュメントの構成・データフロー・スキーマをそのまま採用、API endpoint を Issue 化。

## 11. 関連ドキュメント

- `PHASE_E_PLAN.md`: Plan 工程
- `PHASE_E_DESIGN_B.md`: 落選案 B (L-2 single-table temporal)
- `PHASE_E_DESIGN_C.md`: 落選案 C (L-3 PostgreSQL temporal_tables)
- `PHASE_E_DESIGN_P.md`: 採用案 (本案ベース)
- `bitemporal_audit.md` (MEMORY.md)
