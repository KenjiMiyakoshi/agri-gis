# Phase F Index — 複数レイヤ同時表示 + 組織ベースのレイヤアクセス制御

agri-gis Phase F (`複数レイヤ同時表示 + 組織×レイヤ権限管理`) サイクルの高位サマリ。全 Phase (A/B/C/D/E + D'/E'/C'/H5) 完了後の新規機能拡張サイクル。

## スコープ

ユーザー要望:
- **複数レイヤ重ね表示**: 現状の「ComboBox で 1 レイヤ選択」を「CheckedListBox でオン/オフ切替の複数同時表示」に
- **組織ベースのアクセス制御**: 組織 (organizations) ごとに「閲覧可能レイヤ」「編集可能レイヤ」を持たせる。同組織のユーザは同権限
- **管理 UI**: 管理者向けの組織×レイヤ権限設定画面

複数組織のユーザーが同時利用する本格運用前提の機能拡張。

## 採用方針

| 観点 | 採用 |
|------|------|
| 権限粒度 | **layer 粒度** (feature-level RLS は Phase G 送り) |
| 権限 DB | `org_layer_permission(org_id, layer_id, can_view, can_edit)` 単純 PK + CHECK (`edit ⇒ view`) |
| 既存 layer の backfill | デフォルト `can_view=true / can_edit=false` で全組織に配布 (admin 所属 org は can_edit=true)。後から admin が絞る運用 |
| admin role の扱い | **filter bypass** (全 layer を view/edit 可能)。組織は admin にとって「管理対象」 |
| API filter | `GET /api/layers` を JOIN org_layer_permission で絞る (admin 除く)、`LayerDto.canEdit` を返す |
| Tile 認可 | `/tiles/{layerId}/…` でサーバ側 `can_view` 検査 (深層防御、URL 直叩き対策) |
| WinForms UI | `layerCombo` → `CheckedListBox` 化、`OrgPermissionsForm` 新規 (組織選択 + DataGridView checkbox 2 列) |
| WebGIS UI | `MapContext.layerStack: Map<layerId, TileLayer>`、`addLayer`/`removeLayer`/`setLayerVisible` API |
| バイテンポラル × 権限 | 権限は **現時点のみ** (履歴なし)、asOf には影響しない |
| Phase G 送り | feature-level RLS (異組織の feature が地理的に重なるケース) |
| Phase F' 送り | z-order ドラッグ並べ替え / SSE 単一 connection 統合 / tile cache invalidation on permission change |

詳細は `docs/issues/PHASE_F_PLAN.md`。

## Wave 構成

| Wave | テーマ | 工数 | Issue |
|------|--------|------|------|
| **WF0** | Plan + Design 3 本 | 0.5d | F100 |
| **WF1** | DB: `org_layer_permission` + backfill | 1.0d | F101-F103 |
| **WF2** | API: org フィルタ + 権限管理 endpoint + `WriteFeatureForLayer` policy | 1.5d | F201-F205 |
| **WF3** | WinForms: `CheckedListBox` 化 + `OrgPermissionsForm` | 2.0d | F301-F307 |
| **WF4** | WebGIS: 複数 TileLayer (`layerStack`) | 1.5d | F401-F405 |
| **WF5** | E2E + Docs + Complete サマリ | 0.5d | F501-F502 |
| | **合計** | **約 6.5d** | **25 Issue** |

クリティカルパス約 5.5 営業日。WF3 と WF4 は WF2 完了後に並列可。Phase D'/E' とほぼ同規模。

## 主要 DB 追加

```sql
CREATE TABLE org_layer_permission (
    org_id     INTEGER NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    layer_id   INTEGER NOT NULL REFERENCES layers(layer_id)  ON DELETE CASCADE,
    can_view   BOOLEAN NOT NULL DEFAULT false,
    can_edit   BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (org_id, layer_id),
    CHECK (NOT (can_edit AND NOT can_view))
);
CREATE INDEX ix_org_layer_perm_layer ON org_layer_permission(layer_id);
CREATE INDEX ix_org_layer_perm_view  ON org_layer_permission(org_id) WHERE can_view;
```

## 主要 API 追加

| Method | Path | 状態 |
|--------|------|------|
| GET | `/api/layers` | **org フィルタ + canEdit フィールド追加** (admin 除く、F201) |
| GET | `/api/admin/layers` | admin role は filter bypass (F202) |
| GET | `/api/admin/organizations/{orgId}/layer-permissions` | 新規、組織の権限一覧 (F203) |
| PUT | `/api/admin/organizations/{orgId}/layer-permissions` | 新規、バルク upsert (F203) |
| POST/PATCH/DELETE | `/api/features/*` | `WriteFeatureForLayer` policy で can_edit 検査 (F204) |
| GET | `/tiles/{layerId}/{theme}/...` | `can_view` 検査 (F205、深層防御) |

## 主要 DTO 拡張

```csharp
public sealed record LayerDto(
    int LayerId, string LayerName, string LayerType,
    int? OwnerOrgId, bool IsShared,
    DateTimeOffset CreatedAt, int SchemaVersion, LayerSchemaDto Schema,
    int StyleVersion,
    bool CanEdit  // F201 で追加 (admin は常に true)
);

public sealed record OrgLayerPermissionDto(
    int OrgId, int LayerId, string LayerName, string LayerType,
    bool CanView, bool CanEdit
);
```

## 受け入れ条件 (Phase F 完了の定義)

1. ✅ `0F03_org_layer_permission.sql` 適用、`org_layer_permission` 行数 = `organizations × layers`
2. ✅ general user の `GET /api/layers` が org 許可レイヤのみ返却、`canEdit` 反映
3. ✅ admin の `GET /api/layers` は全件、`canEdit=true`
4. ✅ `POST /api/features` で `can_edit=false` のレイヤに対して 403
5. ✅ `GET /tiles/{layerId}/...` で `can_view=false` のレイヤに対して 403
6. ✅ WinForms 起動 → `CheckedListBox` で複数レイヤ ON → WebGIS に複数 TileLayer 表示
7. ✅ admin で `OrgPermissionsForm` → 組織 ComboBox + DataGridView で view/edit 設定 → 保存 → 該当組織ユーザーに反映
8. ✅ `dotnet test api.tests` 全 green (+ 新規 12 ケース)
9. ✅ `dotnet test windos-app.tests` 全 green (+ 新規 10 ケース)
10. ✅ `dotnet test webgis (vitest)` 全 green (+ 新規 5 ケース)
11. ✅ 全 6 Wave が main にマージ済
12. ✅ `orchestration_state.md` メモリ更新

## Phase F' 申し送り

- レイヤの z-order ドラッグ並べ替え UI
- SSE 単一 connection に統合 (`/api/events/stream-all`)
- tile cache invalidation on permission change (権限変更時に WebGIS の TileLayer を強制再生成)
- 「共有レイヤ」 (`is_shared=true`) の細粒度設定 (現状は全組織 view 可)
- バルク権限編集 (複数組織まとめて設定)

## Phase G 申し送り

- **feature-level RLS** (Row Level Security): 異組織の feature が地理的に重なるケースで、tile 上に他組織の feature が映る問題。`feature_current.org_id` 列 + RLS policy で組織分離
- マルチテナント完全分離 (DB スキーマ分離、テナント毎の DB)
- 共有レイヤの細粒度権限 (組織グループ単位)

## 関連ドキュメント

- `PHASE_A_INDEX.md` 〜 `PHASE_E_INDEX.md`
- `PHASE_D_PRIME_COMPLETE.md` + `PHASE_E_PRIME_COMPLETE.md` + `PHASE_C_PRIME_COMPLETE.md` + `PHASE_H5_COMPLETE.md`
- `docs/issues/PHASE_F_PLAN.md`
- `docs/issues/PHASE_F_WAVE_PLAN.md`
- `docs/issues/PHASE_F_ISSUES_INDEX.md`
- `docs/org-layer-permission.md` (Design)
- `docs/multi-layer-display.md` (Design)
- `docs/phase-f-migration-numbering.md` (Design ノート)

## 関連メモリ

- `orchestration_state.md` — 進捗
- `architecture.md` — ハイブリッド構成
- `stacked_pr_pitfall.md` — base=main 固定
- `smart_app_control_pitfall.md` — WinForms Release 構成
