# Phase F Migration Numbering 判断記録

Phase F の DB マイグレーション番号を `0F03_*` で統一する判断の記録。

## 既存番号体系

```
000_              — Phase 0 (PoC)
0xx_              — Phase A/B (連番)
0A0x_             — Phase A 拡張 (0A04 = C1 修復、0A05 = C2 修復)
0B0x_             — Phase B (なし、連番で済)
0C0x_             — Phase C (なし、連番で済)
0D0x_             — Phase D (4 本: 0D01 - 0D04)
0E0x_             — Phase E (7 本: 0E01 - 0E07)
0E08              — Phase E' (deleted_at DROP)
0F0x_             — Phase D' / E'
                   - 0F01: fn_feature_batch_update (Phase D' WD'1)
                   - 0F02: notify_invalidation (Phase D' WD'3)
```

## Phase F の選択肢

### A: `0F03_org_layer_permission.sql` で `0F0x` 系を継続使用 (採用)

- 利点: 連番で参照しやすい、Phase F は機能拡張サイクルなので 0F 系の延長と解釈可
- 欠点: 0F が「Phase D' / E'」を意味することと、Phase F の「F」が同じ文字なので混同しやすい

### B: 新接頭辞 `0G01_org_layer_permission.sql`

- 利点: 「Phase F = 0G」の明示的対応 (A=0A, B=0B, ..., E=0E, F=0G ? 不規則)
- 欠点: 既存規則からのスキップが必要、Phase G (Row Level Security) で別接頭辞が必要に

### C: `0F03_phase_f_org_layer_permission.sql` (説明文込み)

- 利点: ファイル名で Phase 明示
- 欠点: 長い、既存規則からの逸脱

## 採用: A 案 (`0F03_*`)

理由:
- 既存の連番規則を維持 (`0F01` Phase D' batch / `0F02` Phase D' SSE / `0F03` Phase F org_layer_permission)
- Phase 名と接頭辞の対応は厳密ではない (`0E08` も Phase E' でしか使われてない)
- マイグレーション順序が重要なので、連番が直感的

## 結論

```
db/migration/
  0F01_fn_feature_batch_update.sql       (Phase D' WD'1)
  0F02_notify_invalidation.sql           (Phase D' WD'3)
  0F03_org_layer_permission.sql          (Phase F WF1) ← 本 PR
  0F03_fn_org_layer_perm_upsert.sql      (Phase F WF1)
```

Phase F'/G で更にマイグレーションが増えた場合は `0F04_*` 以降を引き続き使う。

## 関連

- `db/migration/README.md` (migration 一覧、本 PR で更新)
