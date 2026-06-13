# Phase LG' Plan (組織独立ツリー + 複数選択 D&D)

Phase LG (レイヤグループ + レイヤフラグ) の 2 件の積み残しを消化するサイクル。

1. **組織ごとの独立ツリー** — Phase LG の設計漏れ修正。`layer_group` に組織スコープが
   無く全組織共通の単一ツリーになっていた。組織ごとに完全独立したデフォルトツリーへ。
2. **CTRL 複数選択 + まとめ D&D** — ツリー上で Ctrl/Shift により複数レイヤを選択し、
   まとめてグループ間移動・並べ替えできる UX。

## 背景

Phase LG WLG1 で `layer_group` を作った際、PLAN の文言は「組織デフォルトツリーを admin が
管理」だったが、テーブルに `org_id` 列を入れ忘れた。結果、現状は:

- `layer_group` は組織非依存の単一グローバルツリー
- admin が作ったグループが全組織のユーザに見える (`org_layer_permission` の可視制御と無関係)

これは「原則組織単位のデフォルトを admin が管理」(2026-06-12 ユーザ判断) と矛盾する。
本サイクルで正す。

レイヤは組織横断で共有されうる (`org_layer_permission` で org A・B が同一レイヤを閲覧可)
ため、「同じレイヤを org A は『賦課』、org B は『測量』に置く」を許す **完全独立ツリー**
を採用 (2026-06-13 ユーザ判断)。

## ユーザ判断 (確定済)

| # | 論点 | 決定 |
|---|------|------|
| 1 | org デフォルトの粒度 | **組織ごとに完全独立ツリー** (`layer_group.org_id` + `layer_group_member(org_id, layer_id, group_id, order)`) |
| 2 | 進め方 | **別サイクルで両方** (Phase LG を WLG4 で締めた後、本 Phase LG' で org 修正 + 複数選択) |

## 設計の柱

### 1. DB: 組織スコープ化 (`0LGP01`)

```sql
-- グループ定義を組織所有に
ALTER TABLE layer_group ADD COLUMN IF NOT EXISTS org_id INT NULL REFERENCES organizations(id);
-- 既存行は既定組織 (code='default') へ backfill してから NOT NULL 化
UPDATE layer_group SET org_id = (SELECT id FROM organizations WHERE code='default' AND deleted_at IS NULL)
  WHERE org_id IS NULL;
ALTER TABLE layer_group ALTER COLUMN org_id SET NOT NULL;

-- 組織×レイヤの配置テーブル (ツリー上の位置)
CREATE TABLE IF NOT EXISTS layer_group_member (
    org_id     INT NOT NULL REFERENCES organizations(id),
    layer_id   INT NOT NULL REFERENCES layers(layer_id) ON DELETE CASCADE,
    group_id   INT NULL REFERENCES layer_group(group_id) ON DELETE SET NULL,  -- NULL = ルート直下
    sort_order INT NOT NULL DEFAULT 0,
    PRIMARY KEY (org_id, layer_id)
);

-- 既存 layers.group_id / sort_order を既定組織の member へ移送 (冪等)
INSERT INTO layer_group_member (org_id, layer_id, group_id, sort_order)
SELECT (SELECT id FROM organizations WHERE code='default' AND deleted_at IS NULL),
       layer_id, group_id, sort_order
FROM layers
ON CONFLICT (org_id, layer_id) DO NOTHING;
```

- `layers.group_id` / `layers.sort_order` は **deprecated** (後方互換で残置、物理削除は LG'')。
  以後の真実源は `layer_group_member`
- `group_id IS NULL` の member 行 = そのレイヤを **当該組織のルート直下**に置く + 順序を保持
- member 行が無いレイヤ (= `org_layer_permission` で閲覧可だが未配置) → クライアント側で
  ルート末尾デフォルト (Core の Merge 規則と同じ精神)

### 2. API: 全 group endpoint を org スコープ化

- `GET /api/layer-groups` → `WHERE org_id = {ICurrentUser.OrgId}` のみ返す
- `POST /api/admin/layer-groups` → `org_id = ICurrentUser.OrgId` を強制 (body で org 指定不可)
- `PATCH/DELETE /api/admin/layer-groups/{id}` → 自 org の group のみ操作可 (他 org は 404)
- `PUT /api/admin/layers/{layerId}/group` → `layer_group_member` を upsert (自 org)。
  循環/存在検証は従来通り
- `GET /api/layers` の `groupId` / `sortOrder` → 自 org の `layer_group_member` を LEFT JOIN
  (member 無し = groupId:null / sortOrder:0)
- admin は **自組織のツリーのみ管理**。クロス組織管理 (super admin) は本サイクル対象外 (LG'' 候補)

### 3. Core: `LayerTreeModel` 本体は無変更

- defaultTree の構築入力 (`TreeGroupDefinition` / `TreeLayerPlacement`) を、
  org 別 `layer_group_member` + `layer_group` から組むだけ。これは WinForms Controller の責務
- Merge / DFS / シリアライズ ロジックは Phase LG WLG2 のまま再利用

### 4. WinForms: 複数選択 + まとめ D&D

#### 4a. 複数選択 (`LayerTreeView`)
- `SelectedNodes` (順序付き集合) を導入。native TreeView は単一選択のため自前管理
- マウス: 通常 Click=単一 / Ctrl+Click=トグル追加 / Shift+Click=範囲 (可視 DFS 順で from..to)
- owner-draw: 選択ノードを全てハイライト背景描画
- checkbox 列のヒットは従来通り (選択と独立、トグルは即時)

#### 4b. まとめ D&D
- drag 開始時、ドラッグ対象 = `SelectedNodes` (掴んだノードが選択外なら単一にリセット)
- ghost form に「N 件のレイヤ」表示 (N>1 時)
- drop: 選択レイヤ群を **選択時の相対順を保ったまま** 連続挿入
  - drop 先がグループ内 → 全員そのグループへ + 挿入位置から連番
  - drop 先がルート → 全員ルートへ
  - グループノード自体の複数選択移動は MVP 対象外 (group は単独移動のまま)。
    layer + group 混在選択時は **layer のみ移動対象**
- index ずれ補正: 複数レイヤを元位置から除去 → 挿入。Phase F'/LG の単一 drop 知見 +
  「除去で後続 index がずれる」を選択数分まとめて処理 (Core 側 `MoveLayers(ids, parent, startOrder)`
  を新設し UI から呼ぶ。複数移動のアトミック性と順序保証を Core でテスト可能に)

## 工数見積

| Wave | 内容 | 工数 |
|------|------|------|
| WLGP0 | Plan + Design | 0.5d |
| WLGP1 | DB (org_id + member + backfill) + API org スコープ + api.tests | 1.5d |
| WLGP2 | WinForms Controller/ApiClient を member 経路へ + Core `MoveLayers` + tests | 1.0d |
| WLGP3 | LayerTreeView 複数選択 + まとめ D&D + tests | 1.5d |
| WLGP4 | E2E + docs + memory | 0.5d |
| **合計** | | **5.0d** |

クリティカルパス: WLGP0 → WLGP1 → WLGP2 → WLGP3 → WLGP4 (ほぼ直列。WLGP2/WLGP3 は
同じ Controller/LayerTreeView を触るためコンフリクト回避で直列推奨)。

## リスク

| # | リスク | 緩和 |
|---|------|------|
| R1 | `org_id` NOT NULL 化で既存行が落ちる | backfill UPDATE 後に SET NOT NULL。既定組織 id は migration 内 SELECT で解決 |
| R2 | backfill 冪等性 | `ON CONFLICT DO NOTHING` + `ADD COLUMN IF NOT EXISTS`。再実行安全 |
| R3 | `layers.group_id` と `member` の二重真実源 | member を唯一の真実源とし、group_id は読まない (deprecated コメント)。LG'' で drop |
| R4 | 複数選択 D&D の順序/index 崩れ | Core `MoveLayers` をアトミック実装 + windos-app.tests で全件 (グループ跨ぎ/順序保持/混在選択) |
| R5 | Shift 範囲選択の基準 (可視 DFS 順) | `EnumerateVisibleNodesDfs` を LayerTreeView に持たせ、折りたたみグループ内は範囲対象外 |
| R6 | 他 org の group_id を PATCH/DELETE で触る越権 | endpoint で `WHERE org_id=actor.OrgId` 必須。api.tests でクロス org 404 を検証 |

## 関連

- `docs/issues/PHASE_LG_PRIME_WAVE_PLAN.md`
- `docs/issues/PHASE_LG_PLAN.md` (前サイクル、org_id 欠落の原典)
- `docs/PHASE_LG_COMPLETE.md` (WLG4 で作成予定)
- メモリ: `orchestration_state.md` / `stacked_pr_pitfall.md`
