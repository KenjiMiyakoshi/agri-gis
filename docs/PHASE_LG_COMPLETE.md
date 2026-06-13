# Phase LG Complete — レイヤグループ + レイヤフラグ (ツリー UI)

Phase LG (`レイヤ一覧をエクスプローラ風ツリー UI に転換`) の完了サマリ。WLG0〜WLG4 全 Wave マージ済。
フラットな CheckedListBox を owner-draw TreeView (`LayerTreeView`) に置換し、レイヤグループ
(フォルダ概念)・表示/編集/スナップの 3 チェックボックス・drag-and-drop・user_preference 永続化を実装した。

## 達成範囲

| Wave | PR | テーマ |
|------|----|-------|
| WLG0 | #255 | Plan + Wave Plan |
| WLG1 | #257 | DB: `layer_group` migration + API (groups CRUD + レイヤ配置 + LayerDto 拡張) + api.tests |
| WLG2 | #256 | Core: `LayerTreeModel` (UI 非依存ツリー + DFS z-order + マージ規則) + tests |
| WLG3 | #258 | WinForms: `LayerTreeView` owner-draw (3 checkbox + drag-drop) + MainForm ツリー化 + 永続化配線 + tests |
| WLG4 | (本 PR) | E2E シナリオ + Complete サマリ + README 更新 |

## 設計の柱 (確定済の主要選択)

| 論点 | 決定 |
|---|---|
| 組織デフォルトツリー | **DB `layer_group`** を admin が管理 (CRUD endpoint + 監査ログ) |
| ユーザ個別ツリー | **`user_preference` の `layer_tree_v1`** に snapshot 保存 (並べ替え/独自グループ/移動) |
| ネスト | **多階層 OK** — `parent_group_id` 自己参照、循環防止は API レベル検証 (`WITH RECURSIVE` 祖先走査で 422) |
| グループキー名前空間 | `db:N` = admin デフォルト参照 (名前は DB 優先で rename 追従) / `usr:xxxx` = ユーザ独自 (他ユーザ不可視) |
| 編集/スナップ フラグ | **UI + 状態永続化のみ** (`layer_flags_v1`)。機能配線は将来サイクル。tooltip「将来機能」明示 |
| z-order | **可視レイヤを上から DFS 列挙した順** → 既存 `layer_order_change` envelope をそのまま使用 |
| 旧 `layer_order_v1` | deprecated。`layer_tree_v1` 不在時に 1 回限り順序 seed として移行 (`MigrateFromFlatOrder`) |
| WebGIS | **変更なし** — グループは WinForms/サーバ側の概念。地図には従来通り `layer_visibility_change` ×N + `layer_order_change` ×1 が届くのみ |

### Core 層 (`LayerTreeModel`)

UI 非依存 (`System.Windows.Forms` 不参照) のツリーモデルを `windos-app/Core/LayerTree/` に置き、
ツリー構築・移動 (`MoveLayer`/`MoveGroup`)・独自グループ生成 (`CreateUserGroup`)・削除時の
退避 (`RemoveGroup`)・3 値 CheckState (`GetGroupCheckState`)・DFS z-order
(`EnumerateVisibleLayerIdsDfs`)・preference 相互変換 (`ToPreferenceJson`/`FromPreferenceJson`)・
マージ規則 (`Merge`) を全て windos-app.tests でカバーした。寛容動作 (未知 key/重複/循環は
落とさず握り潰す) を徹底し、stale な preference に対する耐性を確保している。

## 受け入れ条件

| # | 条件 | 検証 |
|---|------|------|
| 1 | `layer_group` migration + `layers.group_id`/`sort_order` 追加 | `\d layer_group` + api.tests |
| 2 | groups CRUD + 循環 422 + 非 admin 403 | api.tests `LayerGroupsCrudTests` |
| 3 | レイヤ配置 + グループ削除でルート退避 (SET NULL) | api.tests `LayerGroupAssignTests` + S7 manual |
| 4 | owner-draw 3 checkbox (表示/編集/スナップ) + group 3 値 (Mixed) | windos-app.tests + S3 manual |
| 5 | drag-drop でレイヤ/グループ移動 (青線/青枠 indicator) | windos-app.tests + S2 manual |
| 6 | ツリー構造・展開状態・フラグの `layer_tree_v1`/`layer_flags_v1` 永続化 | windos-app.tests + S5 manual |
| 7 | 旧 `layer_order_v1` からの 1 回限り移行 | windos-app.tests + S1 manual |
| 8 | api.tests 全 green | ✅ 126 pass |
| 9 | windos-app.tests 全 green | ✅ 229 pass |

すべて達成。

## テスト件数

worktree で `dotnet test -c Release` を実行した実数 (Docker / Testcontainers 利用可):

| プロジェクト | Phase F' 完了時 | Phase LG 追加 | 完了時 |
|---|---|---|---|
| api.tests | 116 | +10 (WLG1) | **126 pass** |
| windos-app.tests | 174 | +55 (WLG2 +39 / WLG3 +16) | **229 pass** |
| webgis vitest | 37 | 0 (変更なし) | **37 pass** |
| **計** | 327 | +65 | **392** |

api.tests 126 / windos-app.tests 229 は本 WLG4 ブランチで実測 (`-c Release`、SAC 教訓)。
webgis は Phase LG で変更が無いため Phase F' 完了時の 37 を据え置き (再測なし)。

## Phase LG で発見した非自明な実装課題

WLG1〜WLG3 の最終報告から拾った知見。同種 UI / endpoint を後で触る際の落とし穴メモ:

- **owner-draw TreeView の `WM_LBUTTONDOWN` 横取り** — native `CheckBoxes` は 1 個しか持てないため
  `CheckBoxes=false` + `CheckBoxRenderer` 自前 3 列描画。checkbox 矩形ヒット時は `WM_LBUTTONDOWN`/
  `WM_LBUTTONDBLCLK` を native に渡さず自前トグルのみ発火させ、選択変更・展開トグル・ダブルクリック
  展開の再入を全抑止する (Phase F' の DragAware 教訓 2 の TreeView 版)。
- **`NodeFromRow` 自作** — `TreeView.GetNodeAt` はラベル右側やインデント部分の Y 座標でノードを返さない
  ことがあるため、`TopNode`→`NextVisibleNode` を走査して Y 範囲で行ノードを自前解決する hit-test を用意。
- **PATCH の null セマンティクス** — `parentGroupId` の「未指定 = 変更なし」と「null = ルート直下へ移動」を
  区別するため、DTO バインドではなく `JsonElement` で受けて `TryGetProperty` の presence を見る
  (`COALESCE` だけでは null 移動を表現できない)。`UPDATE ... CASE WHEN @setParent THEN @p ELSE ...` で実現。
- **ProblemDetails の Content-Type 既存バグ** — エラー応答の Content-Type に関する既存挙動が
  api.tests のアサーションに影響。Phase LG の新規 endpoint テストでも `ValidationException`/
  `NotFoundException` の応答形を既存の Errors パイプラインに合わせて検証した。
- **`DbReset` の DELETE + RESTART (TRUNCATE CASCADE 不可)** — `layer_group` は `layers` から
  `ON DELETE SET NULL` で参照されるため、テスト間リセットで安易な `TRUNCATE ... CASCADE` が使えない。
  `DELETE FROM layer_group;` + `ALTER SEQUENCE layer_group_group_id_seq RESTART WITH 1;` で
  シーケンスを明示リセットし、id 連番に依存するテストの並列耐性を担保 (Phase E' のリセット教訓を継承)。
- **asOf 履歴行の `groupId=NULL`** — `PUT /api/admin/layers/{layerId}/group` は `valid_to = '9999-12-31'`
  の active 行のみを対象に `group_id`/`sort_order` を更新する。バイテンポラルの履歴行 (過去断面) は
  グループ概念を持たない (グループは presentation metadata でバイテンポラル対象外) ため、
  asOf クエリで過去断面を引くと `groupId` は NULL 扱いになる。

## Phase LG' 申し送り (次サイクル、最優先)

[docs/issues/PHASE_LG_PRIME_PLAN.md](issues/PHASE_LG_PRIME_PLAN.md) に Plan 済 (WLGP0 #259 マージ済)。

- **【最優先】`org_id` 欠落 → 組織独立ツリーへ修正** — Phase LG の設計漏れ。`layer_group` に
  組織スコープ列が無く、admin が作ったデフォルトツリー (`db:` グループ) が**全組織のユーザに見える**
  単一グローバルツリーになっている (manual-verification S6 参照)。「原則組織単位のデフォルトを admin が
  管理」(2026-06-12 ユーザ判断) と矛盾する状態。LG' で `layer_group.org_id` + `layer_group_member(org_id,
  layer_id, group_id, sort_order)` を導入し、組織ごとに完全独立したツリーへ修正する。同じレイヤを
  org A は「賦課」、org B は「測量」に置ける。
- **CTRL / Shift 複数選択 + まとめ D&D** — ツリー上で Ctrl/Shift により複数レイヤを選択し、
  まとめてグループ間移動・並べ替えできる UX。Core 側に `MoveLayers(ids, parent, startOrder)` を
  新設してアトミックな複数移動 + 順序保証を担保する。

## Phase LG'' 申し送り (LG' の後)

- **`layers.group_id` / `layers.sort_order` の物理削除** — LG' で `layer_group_member` を真実源に
  一本化した後、後方互換で残置した `layers.group_id` / `sort_order` 列を drop する。
- **super admin のクロス組織管理** — LG' では admin は自組織のツリーのみ管理する。複数組織を横断して
  デフォルトツリーを管理する super admin 機能は LG'' 候補。
- **スナップ / 編集フラグの機能配線** — Phase LG では UI + `layer_flags_v1` 永続化のみ。実際の頂点
  スナップ・図形編集モードへの配線は Phase G 以降。

## 関連ドキュメント

- [docs/issues/PHASE_LG_PLAN.md](issues/PHASE_LG_PLAN.md) / [PHASE_LG_WAVE_PLAN.md](issues/PHASE_LG_WAVE_PLAN.md)
- [docs/manual-verification-phase-lg.md](manual-verification-phase-lg.md) (WLG4 E2E シナリオ)
- [docs/issues/PHASE_LG_PRIME_PLAN.md](issues/PHASE_LG_PRIME_PLAN.md) / [PHASE_LG_PRIME_WAVE_PLAN.md](issues/PHASE_LG_PRIME_WAVE_PLAN.md) (次サイクル、org 独立ツリー)
- [docs/PHASE_F_PRIME_COMPLETE.md](PHASE_F_PRIME_COMPLETE.md) (前 Phase 完了サマリ)
