# Phase LG Wave Plan

`PHASE_LG_PLAN.md` の Wave 詳細。ブランチは全て `base=main` (stacked PR 禁止)。

```
WLG0 ── Plan + Design (本 PR)
WLG1 ── DB + API           ┐ WLG0 後に並列可
WLG2 ── Core LayerTreeModel ┘
WLG3 ── WinForms UI (WLG1 + WLG2 マージ後)
WLG4 ── E2E + Docs (WLG3 マージ後)
```

## WLG1: DB + API (1.5d)

ブランチ: `feature/phase-lg-wlg1-db-api`

### LG101: migration `0LG01_layer_group.sql`

- `layer_group` テーブル (PLAN §1 の DDL)
- `layers.group_id` / `layers.sort_order` 追加
- 冪等 (CREATE TABLE IF NOT EXISTS / ADD COLUMN IF NOT EXISTS)
- `down/0LG01_layer_group_down.sql`

### LG102: `GET /api/layer-groups`

- `api/Endpoints/LayerGroupsEndpoints.cs` 新規
- authenticated、フラット一覧 `[{ groupId, parentGroupId, groupName, sortOrder }]`
- DTO: `LayerGroupDto`

### LG103: admin CRUD `POST/PATCH/DELETE /api/admin/layer-groups`

- `api/Endpoints/AdminLayerGroupsEndpoints.cs` 新規 (RequireRole admin)
- POST `{ groupName, parentGroupId?, sortOrder? }` → 201 + Location
- PATCH `{ groupName?, parentGroupId?, sortOrder? }` — parent 変更時は祖先チェーン走査で循環検証 (循環 = 422)
- DELETE — 子 group CASCADE、所属 layer は SET NULL (DB 制約に従う)
- 各操作 Tx 内で `audit_log` INSERT (`action='layer_group_*'`, before/after doc)

### LG104: `PUT /api/admin/layers/{layerId}/group`

- `{ groupId: int?, sortOrder: int }` — groupId=null でルート直下
- 存在しない groupId は 404

### LG105: `GET /api/layers` 拡張

- `LayerDto` に `groupId` / `sortOrder` 追加 (additive)
- SELECT 句 + mapper 更新 (Phase E' の column index shift 教訓: SELECT/WHERE/mapper 3 点同時更新)

### LG106: api.tests

- `Tests/LayerGroups/LayerGroupsCrudTests.cs` — CRUD ハッピーパス + 循環 422 + 非 admin 403
- `Tests/LayerGroups/LayerGroupAssignTests.cs` — layer 配置 + group 削除でルート退避
- `DbReset` に `layer_group` TRUNCATE 追加 (Phase E' の並列耐性教訓)

## WLG2: Core LayerTreeModel (1.0d)

ブランチ: `feature/phase-lg-wlg2-core-tree-model`

### LG201: `windos-app/Core/LayerTree/LayerTreeModel.cs`

UI 非依存 (System.Windows.Forms 不参照、Core 規約準拠)。

- ノード: `TreeGroupNode { Key (db:N / usr:xxx), Name, Order, Expanded, Children }` /
  `TreeLayerNode { LayerId, Order, Visible, EditEnabled, SnapEnabled }`
- 操作: `MoveLayer(layerId, parentKey, order)` / `MoveGroup(key, parentKey, order)` (自己子孫への移動は例外) /
  `CreateUserGroup(name, parentKey)` / `RemoveGroup(key)` /
  `SetGroupVisible(key, bool)` (子孫一括) / `GetGroupCheckState(key)` (3 値)
- `EnumerateVisibleLayerIdsDfs()` — z-order 列挙 (上から DFS)
- シリアライズ: `ToPreferenceJson()` / `FromPreferenceJson(json)` (layer_tree_v1 形式)

### LG202: マージ規則実装

- `LayerTreeModel.Merge(defaultTree, preference, availableLayerIds)`:
  - preference に無い新規 layer → デフォルトツリー位置 (無ければルート末尾)
  - 消滅 layer / 消滅 `db:` グループ → 無視 (中身はルートへ)
  - `db:` グループ名は DB 優先
- 旧 `layer_order_v1` seed: `MigrateFromFlatOrder(order)` (layer_tree_v1 不在時 1 回限り)

### LG203: tests

- `windos-app.tests/Core/LayerTree/` — DFS 順 / 3 値 CheckState / 循環防止 / マージ全規則 /
  シリアライズ往復 / flat order 移行。20 件目安

## WLG3: WinForms UI (2.5d)

ブランチ: `feature/phase-lg-wlg3-winforms-treeview`

### LG301: `LayerTreeView` control

- `windos-app/Forms/LayerTreeView.cs` — TreeView 派生、owner-draw
- 各 layer 行: 表示 / 編集 / スナップ checkbox 3 列 (`CheckBoxRenderer` 描画 + MouseDown hit-test)
- group 行: 表示 checkbox のみ (3 値、混在は `CheckBoxState.MixedNormal`)
- 列ヘッダ Panel ("表示 編集 スナップ")
- 編集/スナップ checkbox に tooltip「将来機能」

### LG302: drag-and-drop

- layer / group ノードの drag 移動 (順序 + 親変更)
- 青線 drop indicator (DragAware 3 教訓踏襲: indicator 読み取り→クリアの順 /
  ItemCheck 相当の抑止 / WM_PAINT overlay)
- group を自分の子孫へ drop 禁止 (カーソル NoMove)

### LG303: MainForm 置換

- `layerList (DragAwareCheckedListBox)` → `layerTree (LayerTreeView)` (Designer 変更)
- `MainFormController`: `LayerTreeModel` 保持に書き換え。
  `OrderedLayerIds` は `EnumerateVisibleLayerIdsDfs()` 委譲で互換維持
- visible toggle → `layer_visibility_change` ×N + `layer_order_change` ×1 (group 一括時も)
- グループ作成/rename/削除のコンテキストメニュー (右クリック)。
  admin はデフォルトツリー編集 (API 呼び出し) と自分用編集をメニューで区別

### LG304: 永続化配線

- `layer_tree_v1` / `layer_flags_v1` の Load (OnLoad) / Save (変更時 best-effort)
- `layer_order_v1` からの初回移行
- `DragAwareCheckedListBox.cs` 削除 (置換完了後)

### LG305: tests

- Controller 層: tree 経由の visibility / z-order / 永続化往復。15 件目安

## WLG4: E2E + Docs (0.5d)

ブランチ: `feature/phase-lg-wlg4-e2e-docs`

- `docs/manual-verification-phase-lg.md` — S1: admin がデフォルトグループ作成 →
  general ユーザに反映 / S2: グループ一括 ON/OFF / S3: ユーザ個別並べ替えが他ユーザに
  影響しない / S4: 編集/スナップ checkbox 永続化 / S5: グループ削除でレイヤ退避 /
  S6: 旧 layer_order_v1 からの移行
- `docs/PHASE_LG_COMPLETE.md`
- README ツリー UI 節
- memory `orchestration_state.md` 更新

## 検証 (各 Wave 共通)

| 項目 | 方法 |
|---|---|
| migration | docker exec psql ON_ERROR_STOP=1 + `\d layer_group` |
| API | `dotnet build` 0 warn + `dotnet test api.tests` (Testcontainers) |
| WinForms | `dotnet build` 0 warn + `dotnet test windos-app.tests -c Release` (SAC 教訓) |
| 実機 | 各 Wave 完了時 smoke、WLG3 後にフル動作確認 |
