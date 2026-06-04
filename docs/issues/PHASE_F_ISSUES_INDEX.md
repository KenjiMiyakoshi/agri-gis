# Phase F Issues Index

Phase F で起票する全 25 Issue の一覧。

ラベル: `phase:F`, `wave:WF{N}`, `area:db|api|webgis|winforms|tests|docs`

## WF0 — Plan + Design

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| F100 | Phase F Plan + Design 3 本 + Index 作成 | docs | 0.5d |

## WF1 — DB: org_layer_permission

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| F101 | `0F03_org_layer_permission.sql` 新規 (DDL + CHECK + INDEX) + down script | db | 0.3d |
| F102 | 既存 layers × organizations の backfill (default view=true / edit=false、admin org のみ edit=true) | db | 0.3d |
| F103 | `fn_org_layer_perm_upsert` 関数 + audit_log 連動 | db | 0.4d |

## WF2 — API: org フィルタ + 権限管理

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| F201 | `GET /api/layers` に org JOIN + `LayerDto.CanEdit` 追加 | api | 0.3d |
| F202 | `GET /api/admin/layers` admin filter bypass | api | 0.2d |
| F203 | `AdminOrgLayerPermissionsEndpoints` 新規 (GET + PUT バルク upsert) | api | 0.4d |
| F204 | `ILayerPermissionService` + `FeatureEndpoints` POST/PATCH/DELETE で can_edit 検査 | api | 0.4d |
| F205 | `TilesEndpoints` で can_view 検査 (深層防御) | api | 0.2d |

## WF3 — WinForms multi-layer + OrgPermissionsForm

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| F301 | `MainForm.layerCombo` → `CheckedListBox` (`layerList`) + bridge envelope | winforms | 0.4d |
| F302 | `MainFormController.VisibleLayerIds` + canEdit による AttributeEditor read-only 連動 | winforms | 0.3d |
| F303 | クリック反応 layer 判定 (payload.layerId 信用) | winforms | 0.2d |
| F304 | `OrgPermissionsForm.cs/Designer.cs` 新規 (組織 ComboBox + DataGridView × 2 CheckBox) | winforms | 0.6d |
| F305 | `LayerAdminForm` に「権限管理」ボタン (admin only) | winforms | 0.1d |
| F306 | `IApiClient`: `GetOrgLayerPermissionsAsync` / `UpdateOrgLayerPermissionsAsync` | winforms | 0.2d |
| F307 | `MainFormControllerMultiLayerTests` 5 + `OrgPermissionsViewModelTests` 5 | tests | 0.2d |

## WF4 — WebGIS multi-TileLayer

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| F401 | `MapContext.layerStack` + `addLayer`/`removeLayer`/`setLayerVisible` | webgis | 0.5d |
| F402 | `layer_visibility_change` envelope handler | webgis | 0.2d |
| F403 | クリックヒット判定の複数 layer 対応 | webgis | 0.4d |
| F404 | SSE `eventStream.ts` 複数 layer 対応 (F' で単一統合) | webgis | 0.2d |
| F405 | `setBaseLayerSource` deprecate → `addLayer` 経路統一 | webgis | 0.2d |

## WF5 — E2E + Docs

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| F501 | docker-compose 動作確認シナリオ (2 組織 × 3 ユーザ) + `docs/manual-verification-phase-f.md` | docs | 0.3d |
| F502 | `PHASE_F_COMPLETE.md` + メモリ更新 + README 更新 | docs | 0.2d |

## 起票時のテンプレート

```markdown
## 課題
(Plan の §X.1 をコピー)

## 採用方針
(Plan の §X.2 採用案をコピー)

## 影響範囲
(Plan の §X.3 をコピー)

## 受入条件
- [ ] (Wave Plan の検証項目)
- [ ] テストが green (`-c Release`)

## 関連
- 親 Wave: WF{N} (#N)
- Design: docs/XXX.md
```

## マイルストーン

`Phase F: 複数レイヤ同時表示 + 組織×レイヤ権限`

## 並列実行の指針

- 各 Wave 内: 同 Wave の独立 Issue は同 PR にまとめる
- Wave 間: WF0 → WF1 → WF2 → (WF3 // WF4) → WF5
- WF3 / WF4 は WF2 (API + LayerDto.canEdit) 完了後に並列着手可
