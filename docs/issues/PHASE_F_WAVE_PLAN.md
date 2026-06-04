# Phase F Wave Plan

## クリティカルパス

```
WF0 (Plan + Design 3 本, 0.5d)
   │
   ▼
WF1 (DB: org_layer_permission + backfill, 1.0d)
   │
   ▼
WF2 (API: org フィルタ + 権限 endpoint + WriteFeatureForLayer, 1.5d)
   │       ┌──────────────────┐
   ▼       ▼                  ▼
WF3 (WinForms multi-layer + OrgPermissionsForm, 2.0d) ⫽ WF4 (WebGIS layerStack, 1.5d)
   │
   ▼
WF5 (E2E + Docs + Complete, 0.5d)
```

合計 6.5d、クリティカルパス WF0 → WF1 → WF2 → WF3 → WF5 = 5.5 営業日。WF4 は WF2 完了後 WF3 と並列。

## WF0 — Plan + Design (Gate)

ブランチ: `feature/phase-f-wf0-design`

| Issue | 内容 | 工数 |
|-------|------|------|
| **F100** | Plan 3 本 + Design 3 本作成 (`PHASE_F_INDEX.md`, `issues/PHASE_F_{PLAN,WAVE_PLAN,ISSUES_INDEX}.md`, `org-layer-permission.md`, `multi-layer-display.md`, `phase-f-migration-numbering.md`) | 0.5d |

成果物:
- `docs/PHASE_F_INDEX.md` (本 PR で作成)
- `docs/issues/PHASE_F_{PLAN,WAVE_PLAN,ISSUES_INDEX}.md`
- `docs/org-layer-permission.md` (F1/F2 Design)
- `docs/multi-layer-display.md` (F3/F4 Design)
- `docs/phase-f-migration-numbering.md` (migration 番号判断記録)

## WF1 — DB: `org_layer_permission` + backfill

ブランチ: `feature/phase-f-wf1-db-org-layer-permission`

| Issue | 内容 | 工数 |
|-------|------|------|
| **F101** | `0F03_org_layer_permission.sql` 新規 (DDL + INDEX + CHECK 制約) + down script | 0.3d |
| **F102** | 既存 layers × organizations 全件 backfill (デフォルト `can_view=true / can_edit=false`、admin 所属 org のみ `can_edit=true`) | 0.3d |
| **F103** | `fn_org_layer_perm_upsert(p_org_id, p_layer_id, p_can_view, p_can_edit, p_actor, p_request_id, p_user_id)` 関数 + audit_log 連動 | 0.4d |

検証:
- migration 適用後 `SELECT COUNT(*) FROM org_layer_permission` = `organizations × layers` 件数
- backfill 後、admin 所属 org の全 layer が `can_edit=true`
- `fn_org_layer_perm_upsert` で値更新 + audit_log INSERT

## WF2 — API: org フィルタ + 権限管理

ブランチ: `feature/phase-f-wf2-api-layer-perm`

| Issue | 内容 | 工数 |
|-------|------|------|
| **F201** | `GET /api/layers` に `JOIN org_layer_permission` 追加 (admin 除く)、`LayerDto.CanEdit` フィールド追加 | 0.3d |
| **F202** | `GET /api/admin/layers` も同じ org フィルタ + admin role bypass (admin は全件返却 + canEdit=true) | 0.2d |
| **F203** | `AdminOrgLayerPermissionsEndpoints.cs` 新規: GET `/api/admin/organizations/{orgId}/layer-permissions` + PUT (バルク upsert) | 0.4d |
| **F204** | `ILayerPermissionService` 新規 + DI 登録 + `FeatureEndpoints.cs` POST/PATCH/DELETE で can_edit 検査 (403) | 0.4d |
| **F205** | `TilesEndpoints.cs` で can_view 検査 (深層防御、URL 直叩き対策) | 0.2d |

検証:
- `dotnet test api.tests -c Release` 全 green + 新規 12 ケース pass
  - org フィルタ (3 ケース)
  - 権限管理 endpoint (3 ケース)
  - WriteFeatureForLayer 403 (3 ケース)
  - tile 403 (3 ケース)

並列度: F201 → F202 並列、F203/F204/F205 は F201 後に独立。

## WF3 — WinForms multi-layer UI + OrgPermissionsForm

ブランチ: `feature/phase-f-wf3-winforms-multi-layer-and-perm-form`

| Issue | 内容 | 工数 |
|-------|------|------|
| **F301** | `MainForm.layerCombo` を `CheckedListBox` (`layerList`) に置換 + Designer.cs 更新 + `ItemCheck` イベント → bridge envelope | 0.4d |
| **F302** | `MainFormController.VisibleLayerIds: HashSet<int>` 管理 + `LayerDto.canEdit` を AttributeEditor の read-only 制御に反映 | 0.3d |
| **F303** | クリック反応 layer の選択ロジック (F 段階は payload.layerId を信用、複数 hit 集約は F') | 0.2d |
| **F304** | `OrgPermissionsForm.cs/Designer.cs` 新規 (組織 ComboBox + DataGridView × 2 CheckBox 列) | 0.6d |
| **F305** | `LayerAdminForm` に「権限管理...」ボタン (admin のみ Visible) → `OrgPermissionsForm` ShowDialog | 0.1d |
| **F306** | `IApiClient` 拡張: `GetOrgLayerPermissionsAsync(orgId)` / `UpdateOrgLayerPermissionsAsync(orgId, dtos)` | 0.2d |
| **F307** | `MainFormControllerMultiLayerTests` (5 件) + `OrgPermissionsViewModelTests` (5 件) | 0.2d |

検証:
- `dotnet build/test windos-app -c Release` 全 green + 新規 10 件 pass
- 手動: 起動 → 複数レイヤ ON → ON/OFF 切替 → 再起動で状態復元
- 手動: 管理メニュー → レイヤ管理 → 「権限管理」→ 組織選択 → CheckBox → 保存 → 一般ユーザでログインし直して反映確認

## WF4 — WebGIS multi-TileLayer (WF3 と並列)

ブランチ: `feature/phase-f-wf4-webgis-multi-layer`

| Issue | 内容 | 工数 |
|-------|------|------|
| **F401** | `MapContext.layerStack: Map<number, TileLayer<XYZ>>` 追加 + `addLayer`/`removeLayer`/`setLayerVisible` 関数 | 0.5d |
| **F402** | `layer_visibility_change` envelope handler (`main.ts`) → addLayer/removeLayer | 0.2d |
| **F403** | クリックヒット判定の複数 layer 対応 — 既存 `getFeaturesAt(layerId)` を `visibleLayerIds` 全件で叩く、最上位 hit を返却 | 0.4d |
| **F404** | SSE `eventStream.ts` を複数 layer 対応 (各 layer ごとに EventSource、F' で単一統合) | 0.2d |
| **F405** | `controllers/layer.ts` の `setBaseLayerSource` を deprecate、`addLayer`/`removeLayer` 経路に統一 | 0.2d |

検証:
- `npm test` (webgis vitest) 全 green + 新規 5 件 pass
- 手動: WinForms で 2 layer ON → WebGIS DevTools の `ctx.map.getLayers()` で TileLayer 2 つ確認
- 手動: 1 layer OFF → 即時除去確認

## WF5 — E2E + Docs + Complete サマリ

ブランチ: `feature/phase-f-wf5-e2e-docs` (or WF4 PR に同梱)

| Issue | 内容 | 工数 |
|-------|------|------|
| **F501** | docker-compose 動作確認シナリオ (2 組織 × 3 ユーザ matrix) → `docs/manual-verification-phase-f.md` | 0.3d |
| **F502** | `docs/PHASE_F_COMPLETE.md` + `orchestration_state.md` メモリ更新 + README 更新 | 0.2d |

## 全 PR

| Wave | ブランチ | base |
|------|---------|------|
| WF0 | `feature/phase-f-wf0-design` | main |
| WF1 | `feature/phase-f-wf1-db-org-layer-permission` | main |
| WF2 | `feature/phase-f-wf2-api-layer-perm` | main |
| WF3 | `feature/phase-f-wf3-winforms-multi-layer-and-perm-form` | main |
| WF4 | `feature/phase-f-wf4-webgis-multi-layer` | main |
| WF5 | `feature/phase-f-wf5-e2e-docs` | main |

すべて `base=main` (`stacked_pr_pitfall` 参照)。マージ順 WF0 → WF1 → WF2 → (WF3, WF4) → WF5 推奨。

## リスク

- **R1 tile 漏洩**: WebGIS の TileLayer 追加だけでは認可されず、URL を直叩きされる可能性 → WF2 F205 でサーバ側 `/tiles` でも `can_view` 検査必須 (深層防御)
- **R2 feature-level RLS 未対応**: 異組織の feature が地理的に重なる場合 tile に映る → Phase G 申し送り、F は layer 粒度のみ
- **R3 バックフィル戦略**: 既存運用 layer の default は `can_view=true` で現状維持を確保 (admin 所属 org のみ can_edit=true)
- **R4 SSE クライアント connection 数**: layer 数 × user で増加 → Phase F' で単一統合
- **R5 バイテンポラル × 権限**: 権限は現時点のみ (履歴なし)、asOf には影響しない (Design に明記)
- **R6 migration 番号**: `0F03_org_layer_permission.sql` で統一 (`0F01/0F02` は Phase D' で消費済)
- **R7 LayerDto.CanEdit breaking change**: TS interface optional `canEdit?` で互換、`undefined => true` (admin 期待値)
- **R8 Designer.cs 改修**: 既存 `layerCombo` を `private` 維持で残し、`layerList` を別フィールドとして追加 → 段階移行
- **R9 admin の filter bypass**: `OrgPermissionsForm` の layer 一覧は admin 経路 (`GET /api/admin/layers?bypassOrgFilter=true` or 専用 endpoint)
- **R10 tile cache invalidation**: 権限変更時の即時反映は Phase F' 送り (現状は cache TTL 内では古い tile が見える)
