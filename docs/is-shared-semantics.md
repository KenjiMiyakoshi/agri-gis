# `layers.is_shared` Semantics Note (Phase F' → Phase G 送り)

`layers.is_shared` フラグの仕様未確定を記録する Design ノート。Phase F' では実装せず、Phase G の Plan サイクルで明確化する。

## 現状

`layers` テーブルに `is_shared BOOLEAN NOT NULL DEFAULT false` 列が存在 (Phase B WB1 で導入)。当初の意図:

> 「組織を跨いで共有される layer (例: 公的測地点 / 行政境界)」を区別するフラグ。

しかし Phase A〜F の実装では:
- `is_shared=true` でも `org_layer_permission` を通じて view/edit 権限が個別に決まる (Phase F)
- WinForms / WebGIS / API のどこにも `is_shared` を特別扱いする経路は無い
- 既存 DB (本番想定) では `is_shared=false` のままがほとんど

つまり、現状 `is_shared` は **未使用フラグ** に近い。

## 仕様候補

Phase G で確定すべき選択肢:

### 候補 A: 完全廃止

- 列 DROP
- 利点: シンプル化
- 欠点: 共有 layer の概念を捨てる

### 候補 B: 「全組織 view 可」のショートカット

- `is_shared=true` の layer は `org_layer_permission` 不要で、全組織が `can_view=true / can_edit=false` 相当
- 利点: 共有 layer の管理が楽 (admin 1 操作で全組織配布)
- 欠点: 既存 `org_layer_permission` との二重管理、competing source of truth

### 候補 C: 「組織グループ」の入り口

- `layer_share_target(layer_id, target_org_id)` 多対多テーブルで「どの組織に共有するか」を保持
- 利点: 細粒度共有
- 欠点: テーブル増 + UI 複雑化、Phase F の単純さを失う

### 候補 D: テナント全公開フラグ (`is_public`)

- `is_shared` を `is_public` にリネーム + 認証なし anonymous read を許可
- 利点: 公的データ公開用途に明確
- 欠点: anonymous read は Phase A の認証必須方針に反する

## 採用 (Phase G で再検討)

Phase F' では何も変更しない。Phase G の Plan サイクルで本ノートを起点に決定する。

候補比較の判定軸:

- 業務要件: 「組織を跨いだ layer 共有」が運用上必要か
- セキュリティ: anonymous read を許す業務領域があるか
- 実装コスト

## 関連

- `PHASE_F_COMPLETE.md` §「Phase F' 申し送り」
- `org-layer-permission.md`
- メモリ: `bitemporal_audit.md` (`is_shared` はバイテンポラル対象外、現時点のみ)
