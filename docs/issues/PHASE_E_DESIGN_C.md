# Phase E Design 案 C — L-3 PostgreSQL temporal_tables 拡張 (落選)

`PHASE_E_PLAN.md` §3.1 の代替案 2。簡潔な比較記録。

## 1. 構成

PostgreSQL の temporal_tables 拡張 (https://github.com/arkhipov/temporal_tables) で `layers` テーブルを SYSTEM VERSIONING 相当に。history table が拡張により自動生成・自動更新される。

```sql
CREATE EXTENSION temporal_tables;

ALTER TABLE layers
    ADD COLUMN sys_period TSTZRANGE NOT NULL DEFAULT tstzrange(now(), null);

CREATE TABLE layers_history (LIKE layers INCLUDING ALL);

CREATE TRIGGER layers_versioning_trigger
    BEFORE INSERT OR UPDATE OR DELETE ON layers
    FOR EACH ROW EXECUTE PROCEDURE versioning('sys_period', 'layers_history', true);
```

style 履歴は案 A の `layer_style_version` を採用 (S-1) するか同様の拡張を `layers.style_json` に適用。

## 2. 案 A との差分

| 観点 | 案 C vs 案 A |
|------|-------------|
| DDL 量 | **大幅減** (`CREATE EXTENSION` + `ADD COLUMN` + `CREATE TRIGGER` の 3 文) |
| 拡張同梱 | **kartoza/postgis:16-3.4 に temporal_tables は未同梱** — dev/本番で別ビルドが必要 |
| asOf クエリ | `SELECT * FROM layers AS OF SYSTEM TIME '2025-04-01'` (拡張提供) — 標準寄りで美しい |
| バイテンポラル制御 | trigger ベースなので **半開区間の閉じ方を自前制御できない** — Phase A C1 修復で痛い目を見た「同日多重更新でゼロ幅区間」のチューニングが効かない |
| Phase A 流儀 | `fn_feature_*` の自前 PL/pgSQL 制御を放棄、trigger 経由のブラックボックスに |
| audit_log 連携 | trigger 内で `audit_log` INSERT を組み込む必要があるが、temporal_tables 提供 trigger は監査ログ非対応 |
| version 楽観ロック | trigger は version 列を扱わない、別途 trigger チェーンを書く |

## 3. 落選理由

1. **拡張同梱なし**: kartoza/postgis image を独自ビルドする運用負荷 (本番 GeoServer 別ホストでも同じ事情) を抱え込むことになる
2. **Phase A 流儀放棄**: `fn_feature_*` を「明示 PL/pgSQL 関数で audit_log 1 件 1 行原則を守る」流儀で築いてきた一貫性を崩す。trigger ベースに切ると Phase A C1/C2 修復の知見が活きない
3. **半開区間の自前制御不可**: Phase A C1 修復で「同日多重更新時のゼロ幅区間 [today, today)」を意図的に許容する設計を取っているが、temporal_tables 提供 trigger ではこの粒度の制御が困難
4. **audit_log との二重メンテ**: trigger 経由の自動 history と `audit_log` 1 件 1 行原則が二重実装になり、整合性検証コストが増える

## 4. 案 C のサブセットとして案 A で取り込む要素

- 案 C の **`AS OF SYSTEM TIME` 構文の美しさ** は API のクエリ string `?asOf=YYYY-MM-DD` で同じ UX を実現済 (案 A)。SQL 構文として PostgreSQL ネイティブにする利点は API レイヤを介すことで相殺

## 5. 案 C を再評価するシナリオ (Phase E' 以降)

- `layer_history` のサイズが TB 級になり、自前 PL/pgSQL の保守工数が `temporal_tables` 拡張の運用工数を上回った場合
- PostgreSQL 公式が SYSTEM VERSIONING (SQL:2011) を組み込む時期が確定し、kartoza image に同梱される場合
- チームに temporal_tables 拡張に熟練したエンジニアが加わり、Phase A 流儀との両立コストが下がった場合

## 6. 関連ドキュメント

- `PHASE_E_DESIGN_A.md`: 採用案
- `PHASE_E_DESIGN_B.md`: 案 B (single-table temporal)
- `PHASE_E_DESIGN_P.md`: 採用案 (案 A ベース)
