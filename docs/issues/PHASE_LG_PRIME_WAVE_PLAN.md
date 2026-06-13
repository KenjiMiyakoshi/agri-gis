# Phase LG' Wave Plan

`PHASE_LG_PRIME_PLAN.md` の Wave 詳細。全ブランチ `base=main` (stacked PR 禁止)。
WLGP1 → WLGP2 → WLGP3 はほぼ直列 (後続が前の成果に依存)。

```
WLGP0 ── Plan + Design (本 PR)
WLGP1 ── DB org スコープ + API (WLGP0 後)
WLGP2 ── Core MoveLayers + WinForms member 配線 (WLGP1 マージ後)
WLGP3 ── 複数選択 + まとめ D&D (WLGP2 マージ後)
WLGP4 ── E2E + Docs (WLGP3 マージ後)
```

## WLGP1: DB org スコープ + API (1.5d)

ブランチ: `feature/phase-lg-prime-wlgp1-org-scope`

### LGP101: migration `0LGP01_layer_group_org_scope.sql` + down
- `layer_group.org_id INT` 追加 → 既定組織 backfill → `SET NOT NULL` (PLAN §1)
- `layer_group_member(org_id, layer_id, group_id NULL, sort_order)` PK(org_id, layer_id)
- 既存 `layers.group_id/sort_order` → 既定組織 member へ `INSERT ... ON CONFLICT DO NOTHING`
- 冪等。down は member DROP + org_id DROP
- `layers.group_id/sort_order` は残置 (deprecated コメント追記のみ)

### LGP102: `GET /api/layer-groups` org フィルタ
- `WHERE org_id = @orgId` (ICurrentUser.OrgId)
- `LayerGroupDto` は変更なし (org_id は内部、レスポンス露出は不要 — 自 org のみ返るため)

### LGP103: admin CRUD の org スコープ
- POST: INSERT に `org_id = actor.OrgId` 強制
- PATCH/DELETE: `WHERE group_id=@id AND org_id=@orgId`。0 件更新 = 404 (他 org 越権を 404 に)
- 循環検証の WITH RECURSIVE も同 org 内に限定

### LGP104: `PUT /api/admin/layers/{layerId}/group` を member upsert へ
- `INSERT INTO layer_group_member (org_id, layer_id, group_id, sort_order) VALUES (...)
   ON CONFLICT (org_id, layer_id) DO UPDATE SET group_id=..., sort_order=...`
- groupId が他 org の group → 404。layerId が閲覧不可 → 404

### LGP105: `GET /api/layers` の groupId/sortOrder を member 経由に
- `LEFT JOIN layer_group_member m ON m.layer_id=l.layer_id AND m.org_id=@orgId`
- member 無し → groupId:null / sortOrder:0
- 旧 `layers.group_id` 直読みを停止 (SELECT 句差し替え、Phase E' の index shift 教訓)

### LGP106: api.tests
- `LayerGroupOrgScopeTests`: org A の group が org B の GET に出ない / org B admin が org A group を PATCH→404 / DELETE→404
- `LayerGroupMemberTests`: 同一 layer を org A=群1 / org B=群2 に配置 → 各 GET /api/layers で別 groupId / member 無し layer は null
- `MigrationBackfillTests` (or 既存 fixture で): 既定組織へ backfill されること
- DbReset に `layer_group_member` クリーンアップ追加

## WLGP2: Core MoveLayers + WinForms member 配線 (1.0d)

ブランチ: `feature/phase-lg-prime-wlgp2-member-wiring`

### LGP201: Core `LayerTreeModel.MoveLayers(IReadOnlyList<int> layerIds, string? parentKey, int startOrder)`
- 複数レイヤをアトミックに移動。元位置から全除去 → parent 配下に startOrder から連番挿入
- 入力順を保持。存在しない layerId は無視。parent が自分自身を含むケースは無い (layer なので)
- 単一 `MoveLayer` は本メソッドへ委譲 (重複排除)

### LGP202: WinForms Controller / ApiClient を member 経路へ
- ApiClient `AssignLayerToGroupAsync` は既存 (PUT layers/{id}/group)。member upsert に内部が変わるだけで I/F 不変
- defaultTree 構築は `GetLayerGroupsAsync` (自 org) + `GetLayersAsync` の groupId/sortOrder (member 由来) から。**Controller のコードは実質無変更** (API が org フィルタ済を返すため)
- 確認: 既存 Controller テストが org フィルタ後も通ること。FakeApiClient を org 別 member 模擬に拡張

### LGP203: tests
- Core: `MoveLayers` 順序保持 / グループ跨ぎ / 一部 unknown id / 単一委譲。10 件目安
- Controller: org 別 defaultTree 構築 + member 配置反映

## WLGP3: 複数選択 + まとめ D&D (1.5d)

ブランチ: `feature/phase-lg-prime-wlgp3-multiselect-dnd`

### LGP301: `LayerTreeView` 複数選択
- `SelectedNodes` (順序付き、List + HashSet) 管理。native SelectedNode は単一のまま併用
- MouseDown: 通常=単一化 / Ctrl=トグル / Shift=可視 DFS 範囲 (`EnumerateVisibleNodesDfs` 自作、折りたたみ内は除外)
- owner-draw: `SelectedNodes` 全てをハイライト (既存単一ハイライトを集合対応に)
- checkbox ヒットは選択と独立 (従来トグル維持)

### LGP302: まとめ D&D
- drag 開始: 掴んだノードが `SelectedNodes` 外なら単一リセット。対象 = 選択集合 (layer のみ抽出)
- ghost: N>1 で「N 件のレイヤ」表示
- DragOver: 既存 above/below/グループ内 + 複数ハイライト維持
- DragDrop (indicator はクリア前に読む — 3 教訓): drop 位置から parentKey + startOrder 算出 →
  `model.MoveLayers(selectedLayerIds, parentKey, startOrder)` → `layer_order_change` ×1 + SaveTreeAsync
- group を自分/子孫へ drop 禁止は単独 group drag 時のみ (複数は layer 限定なので無関係)

### LGP303: tests
- LayerTreeView の選択ロジックは UI 依存だが、可能な範囲で Controller/Core 経由検証
- まとめ移動後の DFS z-order が選択順を保つこと (Core MoveLayers の結合テスト)
- 15 件目安 (大半は Core/Controller 層)

## WLGP4: E2E + Docs (0.5d)

ブランチ: `feature/phase-lg-prime-wlgp4-e2e-docs`

- `docs/manual-verification-phase-lg-prime.md`:
  - S1: org A admin がグループ作成 → org B ユーザに**見えない**こと (設計漏れ修正の確認)
  - S2: 同一共有レイヤを org A/B で別グループに配置 → 各々のツリーで別位置
  - S3: Ctrl で 3 レイヤ選択 → まとめてグループへ D&D → 順序保持
  - S4: Shift 範囲選択 → まとめ並べ替え
  - S5: layer+group 混在選択 → layer のみ移動
- `docs/PHASE_LG_PRIME_COMPLETE.md`
- README 更新 (org スコープ + 複数選択)
- memory `orchestration_state.md` 更新。`layer_group` org_id 欠落 → LG' 修正の経緯を残す

## 検証 (各 Wave 共通)

| 項目 | 方法 |
|---|---|
| migration | docker exec psql ON_ERROR_STOP=1 + `\d layer_group_member` + backfill 行確認 |
| API | `dotnet build` 0 warn + `dotnet test api.tests` (Testcontainers) |
| WinForms | `dotnet build` 0 warn + `dotnet test windos-app.tests -c Release` (SAC 教訓) |
| 実機 | WLGP1 後に org 漏れ解消を smoke、WLGP3 後に複数選択フル確認 |
