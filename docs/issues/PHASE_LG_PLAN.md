# Phase LG Plan (レイヤグループ + レイヤフラグ)

レイヤ一覧をエクスプローラ風ツリー UI に転換するサイクル。レイヤグループ (フォルダ概念)
を導入し、グループ単位の表示 ON/OFF・グループ間のレイヤ移動を可能にする。あわせて
各レイヤに「編集 ON/OFF」「頂点スナップ ON/OFF」チェックボックスを追加する
(本サイクルでは UI + 状態永続化のみ、機能配線は将来サイクル)。

## 背景

Phase F/F' で複数レイヤ同時表示 + z-order 並べ替えまで完成したが、レイヤ数が増えると
フラットな CheckedListBox では整理しきれない。例:「賦課」グループ配下に賦課地区レイヤ・
賦課詳細レイヤをまとめ、グループ単位で一括 ON/OFF したい (ユーザ要望 2026-06-12)。

## ユーザ判断 (確定済 2026-06-12)

| # | 論点 | 決定 |
|---|------|------|
| 1 | グループの所有 | **組織デフォルトを admin が管理 (DB) + ユーザ個別の編集も可能 (user_preference override)** |
| 2 | ネスト | **多階層 OK** (parent_group_id 自己参照) |
| 3 | 編集/スナップ フラグ保存先 | **ユーザ個別 user_preference** (機能は将来、今は UI + 状態保存のみ) |

## 設計の柱

### 1. DB: `layer_group` + `layers.group_id` (組織デフォルトツリー)

```sql
-- 0LG01_layer_group.sql (0F04 の後、ordinal sort で 0LG > 0F)
CREATE TABLE IF NOT EXISTS layer_group (
    group_id        SERIAL PRIMARY KEY,
    parent_group_id INT  NULL REFERENCES layer_group(group_id) ON DELETE CASCADE,
    group_name      TEXT NOT NULL CHECK (length(group_name) BETWEEN 1 AND 100),
    sort_order      INT  NOT NULL DEFAULT 0,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
ALTER TABLE layers
    ADD COLUMN IF NOT EXISTS group_id   INT NULL REFERENCES layer_group(group_id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS sort_order INT NOT NULL DEFAULT 0;  -- 同一親内の表示順
```

- **循環防止は API レベル検証** (parent 変更時に祖先チェーン走査、DB trigger は過剰)
- グループは presentation metadata であり **バイテンポラル対象外** (layer_history に焼かない)
- 監査: admin CRUD endpoint の Tx 内で `audit_log` に `action='layer_group_create/update/delete'` を直接 INSERT
- グループ削除: 子グループは CASCADE、所属レイヤは `ON DELETE SET NULL` でルート直下へ退避

### 2. API

| メソッド | パス | 認可 | 概要 |
|---|---|---|---|
| GET | `/api/layer-groups` | authenticated | 全グループのフラット一覧 (`parentGroupId` 付き、クライアント側でツリー構築) |
| POST | `/api/admin/layer-groups` | admin | グループ作成 `{ groupName, parentGroupId?, sortOrder? }` |
| PATCH | `/api/admin/layer-groups/{id}` | admin | rename / parent 変更 (循環検証) / sortOrder |
| DELETE | `/api/admin/layer-groups/{id}` | admin | 削除 (子 CASCADE、レイヤはルートへ) |
| PUT | `/api/admin/layers/{layerId}/group` | admin | デフォルトツリーでのレイヤ配置 `{ groupId: int?, sortOrder }` |

- `GET /api/layers` レスポンスに `groupId` / `sortOrder` を追加 (additive、破壊変更なし)

### 3. ユーザ個別ツリー (user_preference `layer_tree_v1`)

- 既定は DB のデフォルトツリー。ユーザが並べ替え / グループ間移動 / 独自グループ作成
  した時点で preference に全ツリー snapshot を保存:

```json
{
  "groups": [
    { "key": "db:1",    "name": "賦課",   "parent": null,   "order": 0, "expanded": true },
    { "key": "usr:a1b2", "name": "自分用", "parent": "db:1", "order": 1, "expanded": false }
  ],
  "layers": [
    { "layerId": 5, "parent": "db:1", "order": 0 }
  ]
}
```

- `db:N` = admin デフォルトグループ参照 (**名前は常に DB 側優先**、rename 追従)
- `usr:xxxx` = ユーザ独自グループ (ランダム suffix、他ユーザに影響なし)
- **マージ規則** (ApplyPersistedLayerOrder と同じ精神):
  - preference に無い新規レイヤ → デフォルトツリーの位置 (無ければルート末尾)
  - preference 中の消滅レイヤ / 消滅 `db:` グループ → 無視 (中のレイヤはルートへ)
- **z-order = ツリーの可視レイヤを DFS (上から) で列挙した順** → 既存
  `layer_order_change` envelope をそのまま使用。WebGIS 変更なし
- 旧 `layer_order_v1` キーは deprecated。初回 (layer_tree_v1 不在) はこれを順序 seed に使用

### 4. WinForms: `LayerTreeView` (owner-draw TreeView)

- `TreeView` 派生 + owner-draw で各行に **チェックボックス 3 列 (表示 / 編集 / スナップ)** を描画
  - native CheckBoxes は 1 個しか持てないため `CheckBoxes=false` + `CheckBoxRenderer` 自前描画 + MouseDown hit-test
  - グループ行は「表示」のみ (**3 値: ON / OFF / 混在**)。チェックで子孫レイヤ一括 toggle
  - ツリー上部に列ヘッダ ("表示 編集 スナップ") の小 Panel
- drag-and-drop: レイヤの順序変更 / グループ間移動、グループごと移動。
  drop indicator (青線) は `DragAwareCheckedListBox` の知見を流用
  (**WM_PAINT overlay / indicator 読み取り順 / ItemCheck 抑止の 3 教訓**を踏襲)
- 編集 / スナップ checkbox: 状態のみ保持。`layer_flags_v1` preference に
  `{ "5": { "edit": true, "snap": false } }` 形式で永続化。機能配線は将来サイクル
- **Core 層に UI 非依存の `LayerTreeModel`** (ノード構造 + DFS z-order + preference マージ規則)
  を置き、ロジックは windos-app.tests で全件テスト可能にする

### 5. WebGIS: 変更なし

グループは WinForms / サーバ側の概念。地図には従来通り `layer_visibility_change` ×N +
`layer_order_change` ×1 が届くのみ。

## 工数見積

| Wave | 内容 | 工数 |
|------|------|------|
| WLG0 | Plan + Design | 0.5d |
| WLG1 | DB migration + API (groups CRUD + layer 配置 + LayerDto 拡張) + api.tests | 1.5d |
| WLG2 | Core: LayerTreeModel (ツリー + DFS + マージ) + tests | 1.0d |
| WLG3 | WinForms: LayerTreeView owner-draw + drag-drop + MainForm 置換 + 永続化配線 + tests | 2.5d |
| WLG4 | E2E シナリオ + docs + README + memory | 0.5d |
| **合計** | | **6.0d** |

クリティカルパス: WLG0 → WLG2 → WLG3 → WLG4。WLG1 は WLG0 後に WLG2 と並列可
(WLG3 は WLG1 + WLG2 両方に依存)。

## リスク

| # | リスク | 緩和 |
|---|------|------|
| R1 | owner-draw TreeView の DPI / OS テーマ差異 | `CheckBoxRenderer` / `VisualStyleRenderer` で OS テーマ追従。手描き矩形は避ける |
| R2 | CheckedListBox → TreeView 置換で MainForm の配線全書き換え | WLG2 で Core model を先行確立し、WLG3 は「model ↔ UI 同期」に集中 |
| R3 | ユーザ独自グループと admin デフォルトのマージ衝突 | `db:` / `usr:` key 名前空間分離 + 名前は DB 優先 + 消滅キーは無視 |
| R4 | layer_order_v1 からの移行 | layer_tree_v1 不在時に layer_order_v1 を order seed として読む (1 回限り) |
| R5 | 編集/スナップが「機能しない」ことへのユーザ混乱 | tooltip で「将来機能 (Phase G 以降)」を明示 |
| R6 | グループ多階層の循環 parent | API PATCH で祖先チェーン走査して 422。クライアントも自分の子孫への drop を禁止 |

## 関連

- `docs/issues/PHASE_LG_WAVE_PLAN.md` (Wave 詳細)
- `docs/PHASE_F_PRIME_COMPLETE.md` (前サイクル)
- メモリ: `orchestration_state.md` / `stacked_pr_pitfall.md`
