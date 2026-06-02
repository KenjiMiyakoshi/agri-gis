# Phase B インデックス

Phase B「レイヤ編集 + レイヤインポート」の成果一覧。

## 計画文書

- [PHASE_B_PLAN.md](issues/PHASE_B_PLAN.md) — WBS + 15 設計論点
- [PHASE_B_DESIGN_A.md](issues/PHASE_B_DESIGN_A.md) — 案 A (業界標準フル装備)
- [PHASE_B_DESIGN_B.md](issues/PHASE_B_DESIGN_B.md) — 案 B (段階導入)
- [PHASE_B_DESIGN_C.md](issues/PHASE_B_DESIGN_C.md) — 案 C (既存踏襲)
- [PHASE_B_DESIGN_P.md](issues/PHASE_B_DESIGN_P.md) — **採択案 P** (B ベース + A の DB 投資前倒し)
- [PHASE_B_ISSUES_INDEX.md](issues/PHASE_B_ISSUES_INDEX.md) — 25 Issue 一覧
- [PHASE_B_WAVE_PLAN.md](issues/PHASE_B_WAVE_PLAN.md) — 6 Wave 分割

## 機能仕様

- [layer-import.md](layer-import.md) — レイヤ管理・インポートの仕様と運用
- [auth.md](auth.md) — 認証/認可 (ロールマトリクスに `/api/admin/layers*` 追記)

## Wave 進捗

| Wave | テーマ | PR |
|------|-------|-----|
| WB0 | 性能スパイク (B506) | #138 |
| WB1 | DB 土台 (B101-B104) | #139 |
| WB2 | API 土台 (B201/B202/B205) | #140 |
| WB3 | API バルク + WinForms NuGet (B203/B204/B401/B502) | #141 |
| WB4 | WinForms 本体 (B402-B408) | #142 |
| WB5 | テスト + Docs (B501/B503-B505/B601/B602) | (本 PR) |

## Phase B 完了時の達成

- Phase A の認証/認可と整合した admin 専用レイヤ管理 GUI
- GeoJSON + CSV インポート (C# pure, GDAL 非依存)
- 監査ログ (audit_log) は 1 feature 1 行で C2 修復継承
- Review② H2 (JsonOpts 重複) + H4 (ParentForm キャスト) を同時解消
- 5000 件投入 1.89 秒 (chunkSize=1000)

## Phase C 申し送り

PHASE_B_DESIGN_P.md §9 参照。主要項目:
- `GdalLayerSource` 追加 (Shapefile / MIF / TAB 対応)
- `SHAPE_ENCODING` は `Ogr.Open(path, options)` 経由 (環境変数ではなく)
- 100 万件級向け `fn_feature_bulk_insert` 専用関数の検討
- H5 (MainForm god class) の本格分割
- WebGIS の refresh token / 単体 login UI
- `layer_import_job` 非同期化 + 進捗 polling API
