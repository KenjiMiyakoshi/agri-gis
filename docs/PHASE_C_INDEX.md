# Phase C インデックス

Phase C「Shapefile + GDAL インポート」の成果一覧。

## 計画文書

- [PHASE_C_PLAN.md](issues/PHASE_C_PLAN.md) — WBS + 15 設計論点
- [PHASE_C_DESIGN_A.md](issues/PHASE_C_DESIGN_A.md) — 案 A (完全 GDAL ラッパ + KML 先取り)
- [PHASE_C_DESIGN_B.md](issues/PHASE_C_DESIGN_B.md) — 案 B (Shapefile 先行)
- [PHASE_C_DESIGN_C.md](issues/PHASE_C_DESIGN_C.md) — 案 C (ILayerSource 横展開最小)
- [PHASE_C_DESIGN_P.md](issues/PHASE_C_DESIGN_P.md) — **採択案 P** (B ベース + A の純粋 C# 分離 / 拡張 API / 並列耐性)
- [PHASE_C_ISSUES_INDEX.md](issues/PHASE_C_ISSUES_INDEX.md) — 15 Issue 一覧
- [PHASE_C_WAVE_PLAN.md](issues/PHASE_C_WAVE_PLAN.md) — 5 Wave 分割
- [PHASE_C_C100_POC_RESULT.md](issues/PHASE_C_C100_POC_RESULT.md) — WC0 PoC 結果 (Minimal SKU 含有確認)

## 機能仕様

- [layer-import.md](layer-import.md) — Phase B + C のレイヤ管理・インポート (Phase C セクションあり)
- [auth.md](auth.md) — 認証/認可 (Phase B からの変更なし)

## Wave 進捗

| Wave | テーマ | PR |
|------|-------|-----|
| WC0 | Minimal SKU 実機 PoC | #161 |
| WC1 | GDAL 配線 + GdalLayerSource 骨格 (C101 + C102) | #162 |
| WC2 | ドメイン部品 6 件 (C103-C106 + C301 + C302) | #163 |
| WC3 | ImportWizardForm Step1 inline 統合 (C401) | #164 |
| WC4 | テスト + Docs (C501 / C502 / C504 / C601) | (本 PR) |

## Phase C 完了時の達成

- WinForms に Shapefile zip 取り込み機能 (GDAL 同梱、x64 固定)
- ImportWizardForm Step1 に Shapefile 自動検出 inline 表示 (`SridResolutionState` 4 値、文字コード上書き、手動 SRID)
- 3 値設定駆動 SRID フォールバック (`Reject` / `PromptUser` / `AssumeWgs84`)、`AssumeWgs84` は `audit_log.meta_jsonb.srid_inferred=true` 必須
- `.cpg` fallback 経路で `SHAPE_ENCODING` 環境変数不使用 (xUnit 並列耐性)
- `SridConverter.RegisterWkt` API 公開 (WKT 本体収録は Phase C')
- `[Collection("Gdal")]` + `ICollectionFixture<GdalFixture>` で並列テスト耐性
- API/DB 変更ゼロ (Phase B 確立資産で完結)
- Review② H5 (MainForm god class) は持ち越し、別サイクル扱い

## Phase C' 申し送り (PHASE_C_DESIGN_P §6.12)

PoC で `MapInfo File` / `LIBKML` の Minimal SKU 含有を確認済のため、Full SKU 切替は不要。

1. MIF/MID 対応 (1.5 人日)
2. TAB 対応 (1.5 人日)
3. `IImportPackage` 抽象切り出し (1.0 人日)
4. 和歌山旧測地系等ローカル CS WKT 本体収録 (1.5 人日)
5. `UcsDetectResolver` 実装 (0.8 人日)

合計 5-7 人日 (Phase C 本体 11.5d より小規模)。

## Phase D 申し送り (PHASE_C_DESIGN_P §6.13)

1. KML / KMZ 対応 (LIBKML driver 確認済)
2. GeoPackage / FGB / GPX / DXF
3. `BulkInsertMaxCount` 上限解除 + `fn_feature_bulk_insert` 専用関数判断
4. サーバ側 GDAL (GeoServer / 内製タイラ、[[scale-target-and-server-side-rendering]] と同時設計)
5. ClickOnce / MSIX 差分配布最適化
