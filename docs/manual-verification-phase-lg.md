# Phase LG 動作確認シナリオ (WLG4)

Phase LG (`レイヤグループ + レイヤフラグ`) の手動 E2E 検証。レイヤ一覧をフラットな
CheckedListBox からエクスプローラ風ツリー (`LayerTreeView`) に転換し、グループ
(フォルダ概念)・表示/編集/スナップの 3 チェックボックス・drag-and-drop による
並べ替え/グループ間移動・user_preference 永続化を追加した。

## 前提

- Phase LG の全 PR (WLG0 #255 / WLG1 #257 / WLG2 #256 / WLG3 #258 + 本 WLG4) マージ済
- `0LG01_layer_group.sql` 適用済 (`layer_group` テーブル + `layers.group_id` / `layers.sort_order`)
- API / WebGIS / WinForms 起動済 (Phase F の手順と同じ)
- Phase F の手動検証で作った 2 組織 (既定組織 / 営業部) × ユーザ (admin / salesA) を流用できると
  S6 の確認が楽。無い場合は admin 1 名でも S1〜S5・S7 は確認できる

### 既知の制約 (正直に記す)

**`layer_group` には `org_id` 列が無く、現状はデフォルトツリーが全組織共通の単一グローバル
ツリーになっている。** admin が作ったデフォルトグループ (`db:` グループ) は、組織を問わず
全ユーザに見える (S6 参照)。これは Phase LG の設計漏れであり、次サイクル **Phase LG'**
([docs/issues/PHASE_LG_PRIME_PLAN.md](issues/PHASE_LG_PRIME_PLAN.md)) で `layer_group.org_id`
+ `layer_group_member` を導入して組織独立ツリーへ修正する。本シナリオでは現状のまま検証する。

ユーザ独自グループ (`usr:` グループ) は `user_preference` に保存され**本人のみ**に見えるため、
この制約の影響を受けない。影響するのは admin が作る `db:` グループのみ。

### 確認 SQL の前置き

psql は docker 経由で実行する (コンテナ名は環境に合わせる):

```bash
PSQL="docker exec -i agri_postgis psql -U agri -d agri_gis"   # 適宜読み替え
```

## E2E シナリオ

### S1. ログイン → レイヤツリー表示 + 旧 layer_order_v1 の移行

1. admin で WinForms 起動 → ログイン
2. 右パネルに **レイヤツリー** (`LayerTreeView`) が表示され、上部に列ヘッダ
   「表示 / 編集 / スナップ」の小 Panel がある
3. レイヤがグループ未所属 (DB の `group_id IS NULL`) ならルート直下にフラットに並ぶ
4. **期待 (旧ユーザの移行)**: 過去サイクル (Phase F') で `layer_order_v1` に z-order を保存して
   いたユーザは、`layer_tree_v1` 不在のため初回ログイン時に `MigrateFromFlatOrder` が動き、
   旧フラット順序が**同一親グループ内のレイヤ相対順**として 1 回限り移行される
   (`layer_order_v1` に含まれていたレイヤは Visible=true で復元)

確認 SQL (移行前にユーザがどのキーを持つか):

```sql
SELECT key, jsonb_pretty(value)
  FROM user_preference up JOIN users u ON u.id = up.user_id
 WHERE u.login_id = 'admin'
   AND key IN ('layer_order_v1', 'layer_tree_v1');
```

- 移行前: `layer_order_v1` のみ存在 (旧ユーザ) / どちらも無い (新規ユーザ)
- 何らかのツリー操作で保存が走った後: `layer_tree_v1` が出現する (`layer_order_v1` は deprecated で残置)

### S2. ユーザ独自グループ作成 → レイヤを drag でグループ内へ

1. ツリーの空白部 (または既存グループ上) で**右クリック**
2. コンテキストメニュー「**グループ作成 (自分用)**」を選択
3. 名前入力に「賦課」と入れて OK → ルート直下に「賦課」グループ (太字) が出現
   - グループ上で右クリックして作った場合はそのグループ配下に作られる
4. 既存レイヤ行を「賦課」グループ行へ **drag** する:
   - グループ行の**中央 1/3** にカーソルを置くと、行全体が**青枠 + 半透明ハイライト** (Into) になる
     (上 1/3 = グループの上に挿入、下 1/3 = グループの下に挿入を示す青線 indicator)
5. drop → レイヤがグループ配下に入り、グループを折りたためば隠れる
6. **期待**: 構造変更は即座に `layer_tree_v1` へ best-effort 保存される

確認 SQL (独自グループ + 配置は user_preference に入る。DB の layer_group には現れない):

```sql
SELECT jsonb_pretty(value)
  FROM user_preference up JOIN users u ON u.id = up.user_id
 WHERE u.login_id = 'admin' AND key = 'layer_tree_v1';
-- groups[].key が "usr:xxxxxxxx" (賦課)、layers[].parent が同じ usr: key を指す
```

### S3. グループの表示 checkbox で一括 ON/OFF + Mixed (■) 表示

1. S2 で「賦課」配下にレイヤを 2 つ以上入れる
2. グループ行「表示」checkbox を ON → **子孫レイヤ全部が一括で ON** になり地図に重ね表示
3. グループ配下のレイヤ 1 つだけを OFF にする
4. **期待**: グループ行の checkbox が **Mixed 状態 (■, `CheckBoxState.MixedNormal`)** で描画される
   (一部 ON = 混在)。全部 OFF にすると Unchecked、全部 ON にすると Checked に戻る
5. 可視レイヤの z-order は「ツリーの可視レイヤを上から DFS 列挙した順」で WebGIS に届く
   (`layer_visibility_change` ×N + `layer_order_change` ×1)

確認は UI 上の checkbox 描画 + 地図表示で行う (3 値はセッション状態で DB に保存されない)。

### S4. 編集 / スナップ checkbox を toggle (将来機能)

1. 任意のレイヤ行の「**編集**」checkbox に**マウスを乗せる** →
   tooltip「**将来機能 (現在は状態保存のみ)**」が出る (「スナップ」列も同様)
2. 「編集」checkbox を ON にする
3. **期待**: チェックは付くが、**機能自体は未配線** (地図の編集モードは変わらない)。
   状態は `layer_flags_v1` に保存されるだけ (Phase G 以降で機能を配線する申し送り)

確認 SQL (どちらかが ON のレイヤのみ保存される):

```sql
SELECT jsonb_pretty(value)
  FROM user_preference up JOIN users u ON u.id = up.user_id
 WHERE u.login_id = 'admin' AND key = 'layer_flags_v1';
-- 例: { "5": { "edit": true, "snap": false } }
```

### S5. アプリ再起動 → ツリー構造・展開状態・フラグの復元

1. S2〜S4 の状態 (賦課グループ + 配置 + 一部展開折りたたみ + 編集フラグ ON) を作る
2. グループの展開/折りたたみを 1 回切り替える (展開状態も保存対象)
3. WinForms をいったん終了
4. 同じ admin で再ログイン
5. **期待**:
   - ツリー構造 (賦課グループ + レイヤ配置) が `layer_tree_v1` から復元
   - グループの展開/折りたたみ状態が復元
   - 編集/スナップフラグが `layer_flags_v1` から復元
   - (表示 ON/OFF はセッション状態のため、初回は先頭レイヤ ON の既存挙動。
     可視状態自体は永続化しない設計)

確認 SQL (両キーが残っていること):

```sql
SELECT key FROM user_preference up JOIN users u ON u.id = up.user_id
 WHERE u.login_id = 'admin' AND key IN ('layer_tree_v1', 'layer_flags_v1');
```

### S6. admin がデフォルトグループ作成 → 別ユーザに反映 (org スコープ未対応の既知制約)

1. admin で右クリック → 「**デフォルトグループ作成 (admin)**」を選択
   (このメニューは admin role のみ表示)
2. 名前「グループ」で作成 → `POST /api/admin/layer-groups` が走り、Reload でツリーに `db:` グループとして出現
3. **別組織のユーザ (例: salesA / general)** でログインし直す
4. **期待 (現状の正直な挙動)**: salesA のツリーにも admin が作った「グループ」(`db:`) が**見える**。
   - **これは設計漏れ**: `layer_group` に `org_id` が無いため、デフォルトツリーは全組織共通
   - 本来は「営業部の admin が作ったグループは営業部にのみ見える」べき
   - **Phase LG' で `layer_group.org_id` + `layer_group_member` を導入して組織独立ツリーへ修正する**
     ([docs/issues/PHASE_LG_PRIME_PLAN.md](issues/PHASE_LG_PRIME_PLAN.md))
5. admin が「グループ」を rename すると、`db:` グループ名は**常に DB 側優先**で全ユーザに追従する
   (ユーザが独自に並べ替えていてもグループ名は DB に揃う)

確認 SQL (DB の layer_group には org_id 列が無いことを確認):

```sql
\d layer_group
-- 列は group_id / parent_group_id / group_name / sort_order / created_at / updated_at のみ。
-- org_id は無い (= LG' で追加予定)

SELECT group_id, parent_group_id, group_name, sort_order FROM layer_group ORDER BY group_id;
-- admin が作った「グループ」が 1 行。組織に紐付かない単一グローバルツリー
```

### S7. グループ削除 → 配下レイヤがルートへ退避

#### S7a. 独自グループ (usr:) の削除

1. S2 で作った「賦課」(usr:) グループ上で右クリック → 「**グループ削除**」
2. 確認ダイアログ「中のレイヤと子グループは親へ移動します。」→ はい
3. **期待**: グループが消え、配下のレイヤ/子グループは**元グループがあった位置**へ順序維持で
   退避 (トップレベル削除ならルート直下へ)。`layer_tree_v1` が更新される

#### S7b. デフォルトグループ (db:) の削除

1. S6 で作った「グループ」(db:) 上で右クリック → 「**グループ削除**」 (admin のみ)
2. 確認ダイアログ「全ユーザのデフォルトツリーから削除されます。中のレイヤはルート直下へ
   移動します。」→ はい
3. `DELETE /api/admin/layer-groups/{id}` が走る
4. **期待**: DB 側で子グループは `ON DELETE CASCADE`、所属レイヤは `ON DELETE SET NULL` で
   `group_id` が NULL になりルート直下へ退避する

確認 SQL (db: グループに配置していたレイヤの group_id が NULL になる):

```sql
-- 削除前: 配下レイヤの group_id が当該グループを指す
SELECT layer_id, layer_name, group_id, sort_order FROM layers WHERE valid_to = '9999-12-31' ORDER BY layer_id;

-- 削除後: 同じ layer_id の group_id が NULL に変わる (ルート直下)
SELECT layer_id, group_id FROM layers WHERE valid_to = '9999-12-31' AND group_id IS NULL;

-- 監査ログにも残る
SELECT action, target_table, before_doc, after_doc
  FROM audit_log WHERE action LIKE 'layer_group_%' ORDER BY id DESC LIMIT 5;
```

## 確認チェックリスト

- [ ] S1: ツリー表示 + 旧 `layer_order_v1` を持つユーザは順序が `layer_tree_v1` へ移行
- [ ] S2: 「グループ作成 (自分用)」+ drag でグループ内へ (中央 1/3 = 青枠 Into / 上下 = 青線)
- [ ] S3: グループ表示 checkbox で一括 ON/OFF、一部 ON は Mixed (■) 表示
- [ ] S4: 編集/スナップ checkbox は tooltip「将来機能」+ 状態保存のみ (機能未配線)
- [ ] S5: 再起動でツリー構造・展開状態・フラグが `layer_tree_v1` / `layer_flags_v1` から復元
- [ ] S6: admin デフォルトグループが別ユーザに反映 = ただし **org スコープ未対応で他組織にも見える既知制約**
- [ ] S7: グループ削除で配下レイヤがルートへ退避 (usr: は pref / db: は `group_id` SET NULL)

## 関連

- [docs/issues/PHASE_LG_PLAN.md](issues/PHASE_LG_PLAN.md) / [PHASE_LG_WAVE_PLAN.md](issues/PHASE_LG_WAVE_PLAN.md)
- [docs/PHASE_LG_COMPLETE.md](PHASE_LG_COMPLETE.md) (完了サマリ)
- [docs/issues/PHASE_LG_PRIME_PLAN.md](issues/PHASE_LG_PRIME_PLAN.md) (org 独立ツリー修正 = 最優先申し送り)
- [docs/manual-verification-phase-f-prime.md](manual-verification-phase-f-prime.md) (前サイクル E2E、本シナリオの前提)
