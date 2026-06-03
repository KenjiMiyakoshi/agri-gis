# agri-gis バイテンポラル + asOf クエリ (Phase E 以降)

Phase E 「バイテンポラル全面化」サイクル完了後の asOf 経路の全体俯瞰。

## 1. 設計原則 (Phase A C1/C2 修復から踏襲)

agri-gis は「過去時点復元 (asOf クエリ)」を売りにする汎用 GIS 基盤。Phase A で `feature_*` テーブルに対して確立した以下のイディオムを、Phase E で `layers` / `style_json` にも全面展開する:

| 原則 | 説明 |
|------|------|
| **半開区間** | `[valid_from, valid_to)` でレコードの有効期間を表現。`asOf` は `valid_from <= asOf AND asOf < valid_to` で絞り込む |
| **append-only** | 旧版は history テーブルに退避、本体テーブルは現在版のみ。UPDATE は「旧版 history + 新版 main」の 2-INSERT 1-UPDATE |
| **CURRENT_DATE 接合** | 旧版の `valid_to` と新版の `valid_from` を同じ `CURRENT_DATE` で接合 (= 同日多重更新はゼロ幅区間 `[today, today)` になる、C1 で確認済の仕様) |
| **audit_log 1 件 1 行** | UPDATE/DELETE のたびに 1 行記録。`actor_user_id NOT NULL` (Phase A C2 修復) |
| **DATE 粒度** | `feature_*` (Phase A) と `layers/layer_style_version` (Phase E) はすべて `DATE` 型で統一 |

## 2. データモデル

### 2.1 テーブル構成

```
feature_current ──退避──> feature_history       (Phase A、C1/C2 修復済)
                                              │
                          feature_asof ────────┘  (Phase E E106 0E07)
                          UNION ALL view (GeoServer 参照)

layers ──退避──> layer_history                 (Phase E E101 0E01)
       │
       ├─ layer_schema_version  (Phase A、Phase E では既存維持)
       └─ layer_style_version   (Phase E E103 0E03)
```

### 2.2 主要列

| テーブル | バイテンポラル列 | 楽観ロック | 関係 |
|----------|--------------|----------|------|
| `feature_current` | `valid_from`/`_to` DATE | `version INT` | 1 entity = 1 active row |
| `feature_history` | 同上 + `archived_at` TIMESTAMPTZ | - | append-only |
| `layers` | `valid_from`/`_to`/`version` (Phase E 追加) | `version INT` | 1 layer_id = 1 active row |
| `layer_history` | 同上 + `archived_at`/`archived_by`/`archived_reason` | - | append-only |
| `layer_schema_version` | `valid_from`/`_to`/`schema_version` | - | 履歴別レコード |
| `layer_style_version` | `valid_from`/`_to`/`style_version` | - | 履歴別レコード |

## 3. 関数 (Phase A 流儀踏襲)

| 関数 | 用途 | history 退避 | audit_log |
|------|------|------------|----------|
| `fn_feature_insert/update/delete` | feature の CUD (Phase A) | ✓ | ✓ |
| `fn_layer_create` | layer 新規 (Phase B、変更なし) | - | ✓ |
| `fn_layer_update` (E104) | layer PATCH | ✓ | ✓ |
| `fn_layer_delete` v2 (E105) | layer DELETE (`deleted_at` + `valid_to` 二重書き) | ✓ | ✓ |
| `fn_layer_schema_upsert` | スキーマ変更 (Phase A、変更なし) | - (`layer_schema_version` 自体が履歴) | ✓ |
| `fn_layer_style_upsert` (E106) | style PUT | - (`layer_style_version` が履歴) | ✓ |

## 4. API 経路

### 4.1 asOf 共通パーサ

すべての endpoint で `?asOf=YYYY-MM-DD` を受領。`AsOfParser.TryParse` (`api/Shared/AsOfParser.cs`) を共有。

- DateOnly のみ受領 (Phase A 流儀)
- ISO datetime (`2025-01-01T00:00:00Z`) は **422 ValidationException** (`attributeKey=asOf code=format`)

### 4.2 asOf 対応 endpoint 一覧

| Endpoint | asOf 無し | asOf あり |
|----------|--------|--------|
| `GET /api/layers` | `layers WHERE valid_to='9999-12-31'` | `layers + layer_history` UNION ALL |
| `GET /api/layers/{id}/schema` | `layers` 現在 | `layer_schema_version` 過去 |
| `GET /api/layers/{id}/extent` | `feature_current` 直接 | `feature_asof` view + `valid_from/_to` |
| `GET /api/layers/{id}/at` | 同上 + `ST_DWithin` | 同上 + asOf 絞り込み |
| `GET /api/admin/layers` | active のみ | UNION ALL (`includeDeleted` 無視) |
| `GET /api/admin/layers/{id}/style` | `layers.style_json` 現在 | `layer_style_version` 過去 |
| `GET /tiles/.../?asOf=` | `feature_current` featureType + `max-age=3600` | `feature_asof` featureType + `no-store` |

### 4.3 編集系 endpoint (PATCH/PUT/DELETE)

すべて関数化済み (Phase A 流儀):

- `PATCH /api/admin/layers/{id}` → `fn_layer_update` 経由
  - `If-Match: {version}` ヘッダ任意、なければ内部 SELECT で現 version 取得
  - 02000 → 404 / P0001 → 409 Conflict (`optimistic lock violation`)
- `PUT /api/admin/layers/{id}/style` → `fn_layer_style_upsert` 経由 (Tx 内で GeoServer 同期、失敗時 rollback)
- `DELETE /api/admin/layers/{id}` → `fn_layer_delete` v2 (`deleted_at` + `valid_to` 二重書き、Phase E')

## 5. GeoServer 経路 (Phase D との接続)

### 5.1 featureType 構成

| featureType | source | 用途 |
|------------|--------|------|
| `agrigis:feature_current` | PostgreSQL TABLE `feature_current` | 現在の `/tiles/.../?asOf=` 無しタイル |
| `agrigis:feature_asof` (Phase E 追加) | PostgreSQL VIEW `feature_asof` | asOf 付きタイル |
| `agrigis:selection_layer` | (将来) | selection overlay |

### 5.2 CQL_FILTER

- 現状経路: `CQL_FILTER=layer_id={N}` (Phase D)
- Phase E 履歴経路: `CQL_FILTER=layer_id={N} AND valid_from <= '{asOf}' AND '{asOf}' < valid_to`

### 5.3 Cache 戦略

| 経路 | `Cache-Control` |
|------|----------------|
| `/tiles/.../{z}/{x}/{y}.png` (asOf 無し) | `max-age=3600, public` |
| `/tiles/.../?asOf=YYYY-MM-DD` (Phase E) | `no-store, no-cache, must-revalidate` |
| `/tiles/selection/{sid}/.../{z}/{x}/{y}.png` | `no-store, ...` (Phase D 既存) |

asOf 履歴経路は cache key (`theme, asOf, z, x, y`) で組合せ爆発するため、no-store で容量肥大化を防ぐ。

## 6. WinForms / WebGIS UI

### 6.1 WinForms (`MainForm.cs`)

右パネル上部に「過去時点」モード切替:

```
┌──────────────────┐
│ [ ] 過去時点  [2026-06-03 ▼] │
│ Layer: ...                  │
│ AttributeEditor             │
└──────────────────┘
```

- `asOfEnabled` (CheckBox): 「過去時点」ラベル + チェックボックス
- `asOfPicker` (DateTimePicker): `yyyy-MM-dd`、`asOfEnabled.Checked` 時のみ enabled

挙動:
- `asOfEnabled.CheckedChanged` / `asOfPicker.ValueChanged` → bridge `asof_change` envelope 送出
- 過去時点モード中は `AttributeEditorControl.SetReadOnly(true)` で編集 disable (Phase E: 過去時点の更新は不可)

### 6.2 WebGIS (`controllers/layer.ts`)

`MapContext.currentAsOf: string | null` で状態保持。TileLayer URL に `?asOf=` を付与:

```ts
const qs = asOf ? `?asOf=${encodeURIComponent(asOf)}` : '';
const url = `/tiles/${layerId}/${theme}/{z}/{x}/{y}.png${qs}`;
```

asOf 切替時は OL の `XYZ` source 自体を作り直すことで cache invalidation。

### 6.3 bridge envelope

Host (WinForms) → Web (WebGIS):

```typescript
interface AsOfChangePayload { asOf: string | null; }
// type='asof_change'
```

WebGIS 側 `main.ts` で受領 → `changeAsOf(ctx, p.asOf)` → `loadFeatures(...)` を `currentAsOf` 付きで再呼び出し。

## 7. SLD 履歴サンプル

`layer_style_version` に複数版を持たせると、asOf でその時点の SLD を引き出せる。

```sql
-- 2025-01-15 PUT: 緑色
INSERT INTO layer_style_version (layer_id, style_version, style_json, valid_from, valid_to, ...)
VALUES (1, 1, '{"themes":{"default":{"fillColor":"#4CAF50"}}}', '2025-01-15', '2025-04-01', ...);

-- 2025-04-01 PUT: 黄色 (旧版を closed、新版を active)
UPDATE layer_style_version SET valid_to = '2025-04-01' WHERE layer_id = 1 AND style_version = 1;
INSERT INTO layer_style_version (layer_id, style_version, style_json, valid_from, valid_to, ...)
VALUES (1, 2, '{"themes":{"default":{"fillColor":"#FFEB3B"}}}', '2025-04-01', '9999-12-31', ...);
```

asOf クエリ:
- `GET /api/admin/layers/1/style?asOf=2025-02-15` → 緑色版 (style_version=1)
- `GET /api/admin/layers/1/style?asOf=2025-05-15` → 黄色版 (style_version=2)
- `GET /api/admin/layers/1/style` → 黄色版 (現在 active)

## 8. C1 修復との関係 (同日多重更新)

Phase A C1 で確立した「同日多重更新はゼロ幅区間 `[today, today)`」の仕様は Phase E でも踏襲:

```
2026-06-03 09:00  PATCH (version 1→2)  → layer_history に v1 退避 (valid_from=2026-06-01, valid_to=2026-06-03)
                                       → layers v2 (valid_from=2026-06-03, valid_to=9999-12-31)
2026-06-03 14:00  PATCH (version 2→3)  → layer_history に v2 退避 (valid_from=2026-06-03, valid_to=2026-06-03 = ゼロ幅)
                                       → layers v3 (valid_from=2026-06-03, valid_to=9999-12-31)
```

`?asOf=2026-06-03` クエリは半開区間 `[2026-06-03, 2026-06-03)` には属さないので v2 を引けない (v1 と v3 のみが半開区間でヒット可能、ただし v3 は active なので asOf=today はゼロ幅で範囲外)。

→ **同日多重更新の中間版は asOf では引けない**。これは Phase A C1 と同じ仕様。`audit_log` に履歴は残るので、必要であれば audit_log 経由で過去状態を再構築する。

## 9. `deleted_at` 列の二重書き (Phase E 内)

Phase E では `layers.deleted_at` と `layers.valid_to` の両方を `fn_layer_delete` v2 で書く:

```sql
UPDATE layers
   SET deleted_at = now(),
       valid_to   = CURRENT_DATE,
       updated_at = now()
 WHERE layers.layer_id = p_layer_id;
```

理由:
- 既存 API SQL (`AdminLayersEndpoints.GetLayers` 等) は `WHERE deleted_at IS NULL` を引き続き使う (Phase B 互換)
- Phase E では asOf 経路で `WHERE valid_to = '9999-12-31'` も使う
- 両方の WHERE 句が同じレコードで真になるよう保証

**Phase E' で `deleted_at` 列を撤去**:
1. 全 API SQL の `WHERE deleted_at IS NULL` を `WHERE valid_to = '9999-12-31'::date` に置換
2. `fn_layer_delete` v3 で `deleted_at = now()` を削除
3. `ALTER TABLE layers DROP COLUMN deleted_at`

## 10. Phase D' / Phase E' 申し送り

| 課題 | 補足 |
|------|------|
| `deleted_at` 列 DROP | Phase E' で参照削除 + DROP COLUMN |
| `layer_history` パーティショニング | テーブルサイズ 1000 万行級到達時、年単位 partition |
| MapProxy 永続キャッシュ | Phase D' 候補。asOf 無し経路のみキャッシュ、asOf 有りは引き続き no-store |
| カスタム theme 編集 Web UI | Phase D' 候補。Phase E で `layer_style_version` 履歴化済 |
| `POST /api/features/batch-update` | Phase D' 候補。Phase E で layer/style 履歴化済なので整合可能 |
| WMS GetFeatureInfo 経路 | Phase D MVP 未実装、Phase E では現状の `/layers/{id}/at` 流用 |
| 同日多重更新の中間版回収 | audit_log 経由の再構築 UI (将来) |
| ApiClient asOf 引数追加 | `windos-app/Services/IApiClient` の 4-5 メソッドに `DateOnly? asOf` 追加 (Phase E' Issue 化候補) |
| WinForms MainFormAsOfPickerTests | Phase E' Issue 化候補 (UI test infra が必要) |

## 11. テスト網羅 (Phase E 完了時点)

| カテゴリ | 件数 | 主要テスト |
|---------|------|---------|
| api.tests | **83** | AsOfParserTests / LayerAsOfTests / LayerUpdateBitemporalTests / StyleHistoryTests / TilesAsOfTests + 既存 64 |
| webgis vitest | **16** | client.asof / asofChange envelope + 既存 9 |
| windos-app.tests | 118 (Phase D 維持) | MainFormAsOfPickerTests は Phase E' |

## 12. 関連ドキュメント

- `PHASE_A_INDEX.md` (Phase A 完了) - C1/C2 修復
- `PHASE_B_INDEX.md` (Phase B 完了) - レイヤ管理
- `PHASE_C_INDEX.md` (Phase C 完了) - GDAL インポート
- `PHASE_D_INDEX.md` (Phase D 完了) - サーバラスタタイル
- `PHASE_E_INDEX.md` (本 Phase) - バイテンポラル全面化
- `docs/rendering.md` - 描画アーキ全体 (Phase D + asOf 章は本ドキュメント)
- `docs/deploy/geoserver-prod.md` - 本番別ホスト構成

## 13. 関連メモリ

- `bitemporal_audit.md` — Phase A C1/C2 修復の参照実装
- `rendering_architecture_shift.md` — Phase D 経路
- `architecture.md` — ハイブリッド構成
- `orchestration_state.md` — 進捗状態
