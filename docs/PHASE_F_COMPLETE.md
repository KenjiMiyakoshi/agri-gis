# Phase F Complete — 複数レイヤ同時表示 + 組織×レイヤ権限

Phase F (`複数レイヤ同時表示 + 組織ベースのアクセス制御`) サイクルの完了サマリ。WF0〜WF5 全 Wave マージ済。

## 達成範囲

| Wave | PR | テーマ |
|------|----|-------|
| WF0 | #240 | Plan + Design 7 本 |
| WF1 | #241 | DB: `org_layer_permission` テーブル + backfill + `fn_org_layer_perm_upsert` |
| WF2 | #242 | API: org フィルタ + 権限管理 endpoint + can_edit/can_view 検査 |
| WF3 | #243 | WinForms: `CheckedListBox` + `OrgPermissionsForm` + canEdit 反映 |
| WF4 | #244 | WebGIS: `layerStack` (複数 TileLayer) + per-layer SSE + 複数 hit クリック |
| WF5 | (本 PR) | E2E シナリオ + Complete サマリ + メモリ更新 |

## 設計の柱

### 1. DB レイヤ (`org_layer_permission`)

```sql
CREATE TABLE org_layer_permission (
    org_id     INTEGER NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    layer_id   INTEGER NOT NULL REFERENCES layers(layer_id)  ON DELETE CASCADE,
    can_view   BOOLEAN NOT NULL DEFAULT false,
    can_edit   BOOLEAN NOT NULL DEFAULT false,
    PRIMARY KEY (org_id, layer_id),
    CONSTRAINT chk_org_layer_perm_edit_implies_view
        CHECK (NOT (can_edit AND NOT can_view))
);
```

- バイテンポラル無し (権限は現時点のみ評価、過去状態は `audit_log` に記録)
- CHECK 制約で「edit ⊃ view」を担保
- 部分 index `(org_id) WHERE can_view` で `GET /api/layers` の org フィルタ高速化

### 2. API レイヤ (`ILayerPermissionService`)

- admin role は filter bypass (全 layer に対し can_view/can_edit = true)
- それ以外は `org_layer_permission` を参照
- `GET /api/layers` で `canEdit` フィールドを返却 (LayerDto 拡張)
- `POST/PATCH/DELETE /api/features` で `can_edit` 検査 (403 ProblemDetails)
- `GET /tiles/{layerId}/...` で `can_view` 検査 (深層防御、URL 直叩き対策)
- `PUT /api/admin/organizations/{orgId}/layer-permissions` でバルク upsert (`fn_org_layer_perm_upsert` 経由 + audit_log)

### 3. WinForms UI (`CheckedListBox` + `OrgPermissionsForm`)

- `MainForm.layerCombo` (ComboBox) → `layerList` (CheckedListBox) に置換
- `MainFormController.VisibleLayerIds: HashSet<int>` で ON/OFF 状態を保持
- reload 後も VisibleLayerIds 保持、削除済 layer は集合から除去
- AttributeEditor は 3 条件 OR で read-only: `guest || asOf || !canEdit`
- 管理者向け `OrgPermissionsForm`: 組織 ComboBox + DataGridView × 2 CheckBox 列
  - CHECK 制約のクライアント側 auto-flip (edit ON → view auto ON、view OFF → edit auto OFF)
- `LayerAdminForm` に「権限管理...」ToolStripButton (admin only)

### 4. WebGIS (`layerStack`)

- `MapContext.layerStack: Map<number, TileLayer<XYZ>>` で複数 layer 保持
- `addLayer` / `removeLayer` / `setLayerVisible` / `getVisibleLayerIds` API
- selectionLayer は常にスタック最上位を維持
- `layer_visibility_change` envelope (Host → Web) で WinForms と双方向同期
- per-layer SSE 購読 (`Map<number, EventSource>`)
- クリック → 全 visible layer 並列 `getFeaturesAt`、最上位 hit を採用

## 受け入れ条件

| # | 条件 | 検証 |
|---|------|------|
| 1 | `org_layer_permission` 作成 | WF1 マージ |
| 2 | general user の `GET /api/layers` が org 許可レイヤのみ返却 | api.tests `LayersEndpointOrgFilterTests` |
| 3 | admin の `GET /api/layers` 全件 + canEdit=true | api.tests 同上 |
| 4 | `POST /api/features` で can_edit=false → 403 | api.tests `FeatureEndpointsCanEditTests` |
| 5 | `/tiles/{layerId}/...` で can_view=false → 403 | api.tests `TilesEndpointsCanViewTests` |
| 6 | CheckedListBox で複数 layer ON → WebGIS に複数 TileLayer | webgis `layer.test.ts` + 手動 |
| 7 | `OrgPermissionsForm` で view/edit 設定 → 保存 → 反映 | windos-app.tests `OrgPermissionsViewModelTests` + 手動 |
| 8 | api.tests 全 green (新規 13 件) | ✅ 102 件 pass |
| 9 | windos-app.tests 全 green (新規 10 件) | ✅ 171 件 pass |
| 10 | webgis vitest 全 green (新規 10 件) | ✅ 31 件 pass |
| 11 | 全 6 Wave が main にマージ済 | ✅ |
| 12 | `orchestration_state.md` メモリ更新 | 本 WF5 で |

すべて達成。

## テスト件数 (合計 35 件 新規)

| プロジェクト | 既存 → 新規 | 内訳 |
|---|---|---|
| api.tests | 89 → 102 | 13 件 (LayerPermission 配下) |
| windos-app.tests | 161 → 171 | 10 件 (ViewModels 配下) |
| webgis vitest | 21 → 31 | 10 件 (controllers/layer + messages) |

## Phase F' / G 申し送り

### Phase F'

- レイヤの z-order ドラッグ並べ替え UI
- SSE 単一 connection に統合 (`/api/events/stream-all`)
- tile cache invalidation on permission change (権限変更時に WebGIS の TileLayer 強制再生成)
- 「共有レイヤ」 (`is_shared=true`) の細粒度設定
- バルク権限編集 (複数組織まとめて)
- WinForms クリック時の複数 hit 集約 UI (現状は最上位 layer 1 件採用)

### Phase G

- **feature-level RLS** (Row Level Security): 異組織の feature が地理的に重なるケースで tile に他組織 feature が映る問題。`feature_current.org_id` + RLS policy
- マルチテナント完全分離 (DB スキーマ分離 / テナント毎の DB)
- 共有レイヤの細粒度権限 (組織グループ単位)

## 副次成果

### Migration sort order hotfix (WF2 同梱)

Windows culture-aware sort で `0F03_org_layer_permission_backfill.sql` が `0F03_org_layer_permission.sql` より前に並ぶ問題を発見。`PostgisContainerFixture` を `StringComparer.Ordinal` 化 + `db/migration/README.md` の PowerShell を `Sort-Object -CaseSensitive` 化 + `0F03_fn_*` に `SET check_function_bodies = false` 追加。

### MainFormController 拡張 (WF3)

H5-101 で抽出した `MainFormController` に `VisibleLayerIds` + `GetLayerById` を追加。multi-layer 状態管理を UI 非依存に保持。

### FakeApiClient テスト基盤の柔軟化 (WF3)

`GetLayersAsync` を `GetLayersImpl: Func<...>` デリゲート差替型に変更。今後の controller テストで layer 一覧を柔軟に組み立て可能に。

## 関連ドキュメント

- `PHASE_F_INDEX.md`
- `docs/issues/PHASE_F_PLAN.md` / `PHASE_F_WAVE_PLAN.md` / `PHASE_F_ISSUES_INDEX.md`
- `docs/org-layer-permission.md` (F1/F2 Design)
- `docs/multi-layer-display.md` (F3/F4 Design)
- `docs/phase-f-migration-numbering.md` (migration 番号判断記録)
- `docs/manual-verification-phase-f.md` (F501 E2E シナリオ)
