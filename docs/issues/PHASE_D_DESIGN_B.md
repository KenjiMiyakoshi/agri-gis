# Phase D Design 案 B — MapServer + mapfile (落選)

`PHASE_D_PLAN.md` §3.1 の代替案 1。簡潔な比較記録。

## 1. 構成

- Apache HTTP + MapServer CGI (`mapserv`) を Docker サービスとして同梱
- `mapfile` (独自の DSL) でレイヤ定義 + スタイル定義
- C 製、高速、軽量

## 2. 案 A との差分

| 観点 | 案 B vs 案 A |
|---|---|
| 描画性能 | やや高 (C 実装の優位)。50 万件ベンチで案 A 比 -10〜30% |
| スタイル定義 | mapfile 独自記法 (CLASS / EXPRESSION 等)。Web 上のチュートリアルや既存資産は案 A (SLD) より少ない |
| theme 動的切替 | mapfile は「ファイル丸ごと再 parse」が必要。動的差替は弱い |
| 選択 raster オーバーレイ | OGC FILTER でほぼ同等可能。配線負荷は同程度 |
| Web UI 同梱 | なし (案 A は `web` 同梱で SLD 編集 / レイヤ一覧表示可能) |
| 学習コスト | mapfile DSL の習得 (公式ドキュメント中心、Stack Overflow 答え少) |
| 長期保守 | コミュニティが細る方向 (GeoServer 比) |
| ライセンス | MIT-X (案 A の GPL より自由度高) |

## 3. 落選理由

1. **動的 theme 切替の弱さ**: メモリ `scale_target_and_server_side_rendering.md` で「テーマ別タイルキャッシュ」が重要要件として確定済。mapfile の動的差替は GeoServer の SLD パラメタライズより 1 段重い
2. **Web UI の欠如**: 案 A の GeoServer Web UI が `data_dir/styles/` の編集や `workspaces/layers/` の確認に使えるのに対し、案 B は CLI ベースのみ。Phase D' で「カスタム theme 編集 UI」を作る際の足場がない
3. **コミュニティ・人材**: GeoServer は OSS GIS のデファクト。SLD パターン集や Stack Overflow Q&A が GeoServer 圧勝。チームに WMS 経験者を増やす際の学習リソースが豊富
4. **長期保守**: MapServer はメンテナンス的だが拡張は遅い。GeoServer は OSGeo の主力プロジェクト

## 4. 採用しなかった代わりに

案 B の **MIT-X ライセンスの自由度** という案 A の弱点は、本要件 (Docker プロセス分離 + 別ホスト本番) では実質影響なし (案 A R7 と整合)。

## 5. 案 B を再評価するシナリオ (Phase D' 以降)

- 案 A の数百万件性能ベンチで z=15 タイル > 2s 等の致命的性能問題が露呈し、SLD 最適化でも解決しない場合
- GPL 影響 (案 A 同梱配布) が運用上問題になる場合 (現状: 本番別ホストなので影響なし)
- mapfile DSL に熟練したエンジニアがチームに加わった場合

## 6. 関連ドキュメント

- `PHASE_D_DESIGN_A.md`: 採用案
- `PHASE_D_DESIGN_C.md`: 案 C (自前 SkiaSharp)
- `PHASE_D_DESIGN_P.md`: 採用案 (案 A ベース)
