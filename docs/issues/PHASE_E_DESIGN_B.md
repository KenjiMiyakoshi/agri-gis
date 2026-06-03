# Phase E Design 案 B — L-2 single-table temporal + S-1 (落選)

`PHASE_E_PLAN.md` §3.1 の代替案 1。簡潔な比較記録。

## 1. 構成

`layers` テーブルに `valid_from DATE`, `valid_to DATE` を直接追加。同じ `layer_id` で複数行が時系列に並ぶ single-table temporal。

```sql
ALTER TABLE layers
    DROP CONSTRAINT layers_pkey,
    ADD COLUMN valid_from DATE NOT NULL DEFAULT CURRENT_DATE,
    ADD COLUMN valid_to   DATE NOT NULL DEFAULT '9999-12-31'::date,
    ADD COLUMN version    INTEGER NOT NULL DEFAULT 1,
    ADD CONSTRAINT layers_pkey PRIMARY KEY (layer_id, valid_from);
```

style 履歴は案 A の `layer_style_version` を採用 (S-1)。

## 2. 案 A との差分

| 観点 | 案 B vs 案 A |
|------|-------------|
| テーブル数 | **1 本減** (`layer_history` 不要) |
| FK 構造 | **崩壊**: `feature_current.layer_id REFERENCES layers(layer_id)` が UNIQUE でなくなる。`layer_import_job` / `layer_schema_version` / `layer_style_version` 等の FK 全部張り替え必要 |
| 既存 SQL | **全部書き換え**: `WHERE layer_name='X'` のような単純検索が `WHERE layer_name='X' AND valid_to='9999-12-31'` に。`AdminLayersEndpoints` / `LayerEndpoints` / `TilesEndpoints` / `FeatureEndpoints` の 4 endpoint で SQL 全件監査 |
| 関数命名 | `fn_layer_update` の挙動が「同レコードを update」ではなく「新レコードを insert + 旧レコードの valid_to を更新」になり、Phase A `fn_feature_update` と非対称 |
| asOf クエリ | **シンプル**: `SELECT ... FROM layers WHERE valid_from <= @asof AND @asof < valid_to` 一文 (UNION ALL 不要) |
| ID 系列 | layer_id がもはや「行の identity」ではなく「論理 layer の identity」になる。新規 layer 追加で SERIAL 払い出しても、PK 衝突を避けるための (layer_id, valid_from) 複合化 |

## 3. 落選理由

1. **FK 構造の全壊**: `layer_id` を引いている全ての FK (4 本以上、要 grep) を張り替えるコストが Phase E スコープを膨らませる
2. **Phase A `feature_*` との非対称**: feature は `feature_current + feature_history` の双子構造で確立済。layer だけ single-table 流儀にすると DB 全体で 2 つのバイテンポラル設計が混在 → 引継ぎ時の認知負荷
3. **既存 API クエリの全件監査**: `layer_id` の単純引きが多数の場所にあり、`valid_to='9999-12-31'` 条件追加漏れがリグレッションの温床に
4. **`fn_layer_update` の挙動が直感に反する**: 「テーブル UPDATE」ではなく「INSERT + UPDATE 旧行」になり、Phase A の関数命名 / 挙動規約と乖離

## 4. 採用しなかった代わりに

案 B の **テーブル 1 本減** という案 A の弱点 (DB スキーマ複雑化) は、`layer_history` が独立テーブルとして存在することで logically 補える (`layer_history` は読み取り専用、追記のみ)。

## 5. 案 B を再評価するシナリオ (Phase E' 以降)

- Phase A 流儀との一貫性よりも「テーブル数最小化」を最優先する場合
- PostgreSQL temporal_tables 拡張を導入する余裕ができた場合 (案 C と組合せ)
- `layer_history` のテーブルサイズが過大になり (= 数千万行) パーティショニング戦略を見直す必要が出た場合

## 6. 関連ドキュメント

- `PHASE_E_DESIGN_A.md`: 採用案
- `PHASE_E_DESIGN_C.md`: 案 C (PostgreSQL temporal_tables 拡張)
- `PHASE_E_DESIGN_P.md`: 採用案 (案 A ベース)
