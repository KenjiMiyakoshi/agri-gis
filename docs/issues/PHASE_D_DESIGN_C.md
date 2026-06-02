# Phase D Design 案 C — 自前 .NET (SkiaSharp + NTS) タイルジェネレータ (落選)

`PHASE_D_PLAN.md` §3.1 の代替案 2。簡潔な比較記録。

## 1. 構成

- `AgriGis.Api` プロセスに描画機能を内包
- `SkiaSharp` で PNG ラスタライズ
- `NetTopologySuite` で geometry parsing
- `ProjNet` で CRS 変換 (既に Phase B で導入済)
- スタイル定義は C# クラス + JSON DSL (SLD 相当を自作)

## 2. 案 A との差分

| 観点 | 案 C vs 案 A |
|---|---|
| 描画エンジン信頼性 | SkiaSharp は OL/Mapbox 等が採用、性能は実証済。geometry indexing + tile generation の自作部分が技術的負債候補 |
| Docker Compose | **増設なし** (API プロセス内蔵)。本番別ホスト構成も不要 |
| スタイル定義 | C# DSL or 独自 JSON。SLD 互換性は明示放棄、Web UI も自作 |
| 動的 theme 切替 | C# ホットスワップ。実装柔軟性は **3 案で最高** |
| テスト | 単体テスト書きやすい (C# だけで完結)、案 A の WireMock 代わりに直接 SkiaSharp 動かせる |
| 数百万件性能 | 未知 (geometry index と tile generation キャッシュをゼロから書く必要) |
| 学習コスト | SkiaSharp の習得 + WMS の自作 (キャッシュ / レイヤ / SRS 変換 / SLD 相当) |
| ライセンス | 全て自前なので無問題 |
| 工数 | 案 A の 2-3 倍 (本要件で 20-30 人日想定) |

## 3. 落選理由

1. **工数**: 案 A の 11.5 日に対し 20-30 日想定。Phase D の前後に他の TODO (Phase D' / H5 / UI 改善) があり、長期サイクルを 1 つに集中するのは投資バランス悪い
2. **車輪の再発明**: WMS は標準化された仕様 (OGC) で実装多数。自前で書くと標準準拠検証コストが上乗せ
3. **未知の数百万件性能**: SkiaSharp 自体は速いが、PostGIS から数万 feature を毎タイル取得 → ラスタライズの全体性能は実装次第。案 A は実証済
4. **チーム外説明性**: 「なぜ GeoServer を使わない?」を外部レビュアー / 引継ぎ時に説明し続ける負荷
5. **メモリ整合**: `scale_target_and_server_side_rendering.md` が案 A を本命指定。案 C は「将来検討」リスト

## 4. 案 C を再評価するシナリオ (Phase D' 以降)

- 案 A 採用後の運用で「GeoServer 運用負荷 (data_dir 管理 / GPL 問題 / Docker 起動時間) が無視できない」と判明した場合
- 数百万件以上 (= 1 億件) に達して案 A のスケール限界に達した場合
- 「Web UI 含むカスタム theme 編集体験」を内製しきりたい強い意志 + 工数余裕がある場合

## 5. 案 C のサブセットとして案 A で取り込む要素

- **C# 純粋関数の活用**: 案 A でも `TilesEndpoints` 内の URL 構築や `selection_sets` 操作は C# で書く。案 C の「テスト書きやすさ」の利点を一部取り込める
- **SkiaSharp の余地**: Phase D' で「クライアントサイド軽量描画」(例: 選択枠線のオーバーレイを `<canvas>` で描く) を検討する場合に SkiaSharp の知見が活きる

## 6. 関連ドキュメント

- `PHASE_D_DESIGN_A.md`: 採用案
- `PHASE_D_DESIGN_B.md`: 案 B (MapServer)
- `PHASE_D_DESIGN_P.md`: 採用案 (案 A ベース)
