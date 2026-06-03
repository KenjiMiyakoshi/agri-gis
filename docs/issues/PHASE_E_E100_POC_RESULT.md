# Phase E E100 PoC 結果 — feature_asof view 性能 (50万件 × z=15)

`PHASE_E_DESIGN_P.md` §11 P1 リスク (`feature_current + feature_history` UNION ALL の性能) を 50 万件 fixture で検証した PoC の結果。

## 判定: **GO**

全経路平均応答時間が < 500ms (cold cache) の受け入れ条件を満たした。WE1 着手承認。

## 実施日時

2026-06-03

## fixture 構成

| テーブル | 件数 | 内容 |
|---------|------|------|
| `feature_current` | 500,000 | layer_id=1000、帯広付近 4km × 4km の小ポリゴン (5.7m × 5.7m グリッド) |
| `feature_history` (gen 1) | 500,000 | 同じ entity 群の 2024-01-01 ~ 2024-07-01 履歴版 |
| `feature_history` (gen 2) | 500,000 | 同 2024-07-01 ~ 2025-01-01 履歴版 |
| `feature_asof` VIEW | 1,500,000 | feature_current UNION ALL feature_history |

## 計測タイル

z=15 で帯広付近 (143.205, 42.9115) の 5 タイル: (29417,12051) / (29418,12051) / (29417,12052) / (29418,12052) / (29419,12051)

タイルサイズ 1223m × 1223m、データ密度は 1 タイルあたり約 45,000 件 (端のタイル 29419/12051 は約 18,000 件)。

## 計測結果 (cold cache、各 5 タイル)

| 経路 | featureType | CQL_FILTER | avg | max |
|------|------------|-----------|-----|-----|
| **1. 現状 (Phase D 既存)** | `feature_current` | `layer_id=1000` | **0.350s** | 0.446s |
| **2. 新経路 (asOf 無し相当)** | `feature_asof` | `layer_id=1000 AND valid_to='9999-12-31'` | **0.343s** | 0.410s |
| **3. 履歴経路 (asOf=2024-06-15)** | `feature_asof` | `layer_id=1000 AND valid_from <= '2024-06-15' AND '2024-06-15' < valid_to` | **0.407s** | 0.523s |

## 受け入れ条件との照合

| 条件 | 結果 | 判定 |
|------|------|------|
| 経路 2/3 とも avg < 500ms (cold) | 343ms / 407ms | **PASS** |
| 経路 2/3 とも < 2s | 410ms / 523ms (max) | **PASS** |
| 経路 1 vs 経路 2 のオーバーヘッド | -7ms (改善) | **PASS** (view 経由でもパフォーマンス低下なし) |
| 経路 2 vs 経路 3 のオーバーヘッド | +60ms | **PASS** (履歴 union のコスト許容範囲内) |

## 観察

### feature_asof view (案 A `0E07`) の性能特性

- **view 経由でもオーバーヘッドゼロ** (経路 1 vs 2): PostgreSQL の query planner が UNION ALL を効率的に分解、`valid_to='9999-12-31'` 条件が `feature_history` を完全に枝刈り (feature_current のみ参照)
- **履歴経路は +60ms 程度の追加コスト**: 経路 3 では `feature_history` のスキャンが入るが、`valid_from <= asOf AND asOf < valid_to` の半開区間条件がインデックス効率的に絞る (1/2 の世代しかヒットしない)
- **max は 523ms (cold)** が 1 件だけ受け入れ条件 500ms をわずかに超過。warm cache (Phase D' MapProxy) 入れれば 50ms 切る想定

### Phase D D504 (Phase D 末の性能 smoke) との比較

Phase D D504 では `feature_current` 単体で 50 万件 z=15 タイル < 500ms を確認済。Phase E では history union が +60ms 程度の追加で許容範囲。

## 結論

- **GO**: WE1 (DB 土台) 着手承認
- `feature_asof` view 設計 (案 A `0E07`) はそのまま採用
- Phase E' で MapProxy 永続キャッシュ層を入れれば warm cache 50ms 想定 (Phase D' 課題と整合)
- WE5 末の性能 smoke で再検証 (本番想定の数百万件規模に拡張時の挙動を確認)

## 次アクション

1. PR (本 PR) マージ → main 反映
2. `feature/phase-e-we1-db` branch 作成 → Issue #197-#202 (E101-E106) 着手
3. PoC fixture (`layer_id=1000` の feature_current 50万 + feature_history 100万) は WE1 着手前に DELETE クリーンアップ (本 PoC ブランチで実施済)

## クリーンアップ手順

```sql
BEGIN;
DELETE FROM feature_history WHERE layer_id = 1000;
DELETE FROM feature_current WHERE layer_id = 1000;
DELETE FROM layers WHERE layer_id = 1000;
DROP VIEW IF EXISTS feature_asof;  -- WE1 で改めて CREATE
COMMIT;
```

GeoServer の `feature_asof` featureType は **削除しない** (WE3 で再利用)。

## 関連ドキュメント

- `PHASE_E_PLAN.md` §3.2: ユーザー判断 (WE0 でミニ PoC)
- `PHASE_E_DESIGN_P.md` §11 P1: 本 PoC のリスク仮説
- `PHASE_E_ISSUES_INDEX.md` E100: 受け入れ条件
- `tools/perf/feature-asof-50k/`: 本 PoC のスクリプト一式
