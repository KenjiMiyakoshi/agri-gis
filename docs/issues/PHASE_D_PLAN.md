# agri-gis Phase D Plan — 描画アーキテクチャ転換 (クライアントベクタ → サーバラスタタイル)

Phase A (認証基盤) / Phase B (レイヤ管理 + GeoJSON/CSV インポート) / Phase C (Shapefile + GDAL インポート) 完了後の次サイクル。クライアント側で全 feature をベクタ描画していた現状を、**GeoServer 同梱によるサーバラスタタイル方式**へ転換する。

## 1. 動機

### 1.1 ユーザ要件 (2026-06-02 確定)

- 既存運用の総管理図形数 **数百万件**、レイヤ単体で 50 万件級
- 表示は範囲絞り込みで数千〜数万件
- **編集は稀** で 1〜数筆 (普段クライアントにベクタを持つ必然性なし)
- **複数図形のスタイル変更 (テーマ別カラー、選択ハイライト) は頻繁**
- 編集ワークフローと「数百万件 + 頻繁スタイル変更」が独立した利用パターンを持つ

### 1.2 Phase A/B/C 時点のボトルネック

`GET /api/features?layerId=N` は対象レイヤの全 feature を 1 FeatureCollection JSON で返し、WebGIS (OpenLayers) は `vectorSource.addFeatures` でクライアントメモリに全て積んで描画する。

| ボトルネック | 50 万件レイヤでの影響 |
|---|---|
| PostGIS `ST_AsGeoJSON × N` | DB CPU + I/O が線形 |
| JSON 転送 | 250 MB〜1 GB |
| ブラウザ parse + `readFeatures` | 30〜120 秒 (UI フリーズ) |
| OL 毎フレーム再描画 | 60 fps が 2〜5 fps に |
| メモリ膨張 | 1〜3 GB タブ |

現実的な上限は数千件、頑張って 1〜2 万件。**Phase A/B/C の延長線上では数百万件運用に到達できない。**

## 2. 採用方針 (1 行)

**GeoServer を同梱 + (将来) MapProxy キャッシュを追加**し、`GET /tiles/{layerId}/{theme}/{z}/{x}/{y}.png` を主要描画経路にする。クライアントは TileLayer 表示のみ、編集モード時のみ単一 entity をベクタ取得。

## 3. Plan 工程の経過

### 3.1 Plan エージェント出力 (2026-06-02)

Plan エージェント (Plan subagent_type) が 3 候補を比較し A 案を推奨:

| 案 | 構成 | 推奨度 |
|---|---|---|
| **A. GeoServer + MapProxy** | Docker Compose に geoserver サービス追加 + SLD ファイル | **採用** |
| B. MapServer + mapfile | CGI 同梱、C 製高速サーバ | 落選 (長期保守性、mapfile 学習コスト) |
| C. 自前 .NET SkiaSharp | API プロセスに描画機能を内包 | 落選 (人月、SLD 相当の再実装) |

採用理由:
- メモリ `scale_target_and_server_side_rendering.md` が既に GeoServer 同梱を本命指定
- 数百万件 + 選択 raster overlay の両方を WMS パターンで吸収可能
- MapProxy はキャッシュ key を `(theme, z, x, y)` に揃えるだけで bbox 無効化が素直
- OSS 標準、人材豊富

### 3.2 ユーザ判断 (2026-06-02)

Plan エージェントが提示した 5 論点のうち 4 件を確定:

| 論点 | 決定 | Phase D での影響 |
|---|---|---|
| GeoServer 配置 | **dev=Compose / 本番=別ホスト (k8s/VM)** | WD1 は dev Compose 拡張のみ。本番手順は `docs/deploy/geoserver-prod.md` (WD5 作成) |
| `?layerId=` Sunset 期間 | **WD3 完了時点で即 410** | WD3 で WebGIS 切替必達、WD5 で `?layerId=` 依存テスト書き換え (+1.5d) |
| SLD 保管場所 | **DB `layers.style_json` を初期から** | WD1 で migration、WD2 で `GET/PUT /api/admin/layers/{id}/style`、API → GeoServer に SLD push (or proxy) |
| 選択 sid TTL/認可 | **セッション終了まで + 発行ユーザのみ取得可能** | DB `selection_sets(sid, user_id, entity_ids[], created_at)` 新設、JWT user_id とマッチ、Redis 不要 |

未確定 1 件 (theme 切替の role 制限) は WD2/WD3 着手時に決定。

## 4. スコープ (Phase D で対応する範囲)

### 4.1 含む

- GeoServer 同梱 (dev docker-compose) + 初期 data_dir + SLD 配信パイプライン
- DB migration: `layers.style_json` JSONB + `selection_sets` テーブル + `user_sessions` テーブル (sid 紐付け用、選択ではなく Phase D で初投入)
- API 新設: `/tiles/{layerId}/{theme}/{z}/{x}/{y}.png` proxy、`POST /api/selection` (sid 発行)、`GET /tiles/selection/{sid}/{z}/{x}/{y}.png`、admin theme CRUD (`GET/PUT /api/admin/layers/{id}/style`)
- API 廃止: `GET /api/features?layerId=` (Sunset → 410)、`IApiClient.GetFeaturesAsync` 削除
- WebGIS 描画: `VectorSource` → `TileLayer` 主役切替、選択は 2 段 (OL Style 暫定 → サーバタイル差替)
- WinForms 連携: `feature_clicked` → `POST /api/selection` → bridge `selection_overlay_ready` + `LayerSelectPayload.theme` 追加
- テスト書き換え: `?layerId=` 依存テストを `{entityId}` ベース or DB 直接 SELECT に
- ドキュメント: `docs/rendering.md` (Phase D アーキ解説) + `docs/deploy/geoserver-prod.md` (本番別ホスト構成)
- 性能 smoke: 50 万件レイヤで z=15 タイル < 500ms

### 4.2 含まない (Phase D' 以降の申し送り)

- MapProxy 永続キャッシュ層 (WD1 では GeoServer の内部キャッシュのみ。MapProxy は Phase D' で導入)
- カスタム theme 編集 Web UI (Phase D は admin API のみ提供。GUI は Phase D')
- `POST /api/features/batch-update` 一括属性編集 (Phase D' or 別サイクル)
- MVT (ベクタタイル) 経路 (本要件と不一致、メモリ参照)
- `windos-app.tests` の SAC 回避 Release 強制化以外の CI 改善
- H5 (MainForm god class) リファクタ (独立サイクル候補のまま)
- ImportWizard Required 手動トグル UI (Phase D とは無関係、独立小サイクル)

## 5. 既存資産との整合

| 観点 | 状態 |
|---|---|
| 認証/認可 (JWT + 3 ロール) | 変更なし。GeoServer 経由 tile 取得も Bearer 必須に統一 |
| バイテンポラル + audit_log | 変更なし。GeoServer は `feature_current` のみ参照 (asOf は API 経由) |
| PostGIS スキーマ | `layers.style_json` + `selection_sets` 2 件追加。既存テーブルは不変 |
| Phase B `ILayerSource` 契約 | 変更なし (インポート経路には触らない) |
| Phase C GDAL 経路 | 変更なし (インポート時の OGR Transform 修正は Phase C で完結) |
| WinForms WebView2 bridge | envelope 拡張 (`features_selected`, `theme_change`, `selection_overlay_ready`) |
| WebGIS `vectorSource.addFeatures` | WD3 完了時点で削除 |

## 6. リスクと申し送り (Plan 工程で識別)

| # | リスク | 緩和策 |
|---|---|---|
| R1 | SLD 学習コスト (チーム未経験) | WD0 で基本 2 パターン + WD5 で SLD パターン集 5 例の docs。Phase D' で「カラーランプ UI」を申し送り |
| R2 | Docker Compose 起動時間 +20-40s で CI smoke が flaky 化 | api.tests は geoserver に依存させず、tile proxy だけ HttpClient モック化 (WD5 で wiremock 検討) |
| R3 | `?layerId=` 依存テストの書き換え工数 | WD0 で grep 計上、WD5 で集中対応 (+1.5d 込み済) |
| R4 | 移行期間中のクライアントベクタ経路並存 | WD3 内で完結 (Sunset 即 410 方針)。`?debug=vector` 等のバックドアは作らない |
| R5 | 編集→タイル無効化の遅延 (楽観的更新 UX) | Phase D' 申し送り。WD3 では「保存後に強制リロード」で最低限対応 |
| R6 | 本番 GeoServer の認証パススルー | WD1 で API → GeoServer 内部認証 (basic auth on docker network)。本番別ホストでも同パターン |
| R7 | GeoServer の GPL ライセンス | プロセス分離 (別 Docker コンテナ) なので影響軽微。本番別ホスト構成でも同様 |

## 7. 工数概算

| Wave | 工数 |
|---|---|
| WD0 (PoC Gate) | 0.5-1.0d |
| WD1 (Infra + migration) | 2.0d |
| WD2 (API) | 2.5d |
| WD3 (WebGIS) | 2.0d |
| WD4 (WinForms) | 1.0d |
| WD5 (Tests + Docs) | 3.0d |
| **合計** | **約 11-11.5d** |

Phase B (11.5d / 11 営業日) と同等規模。Phase C (11.5d / 8-9 営業日) よりリスク高 (新インフラ + 既存テスト書き換え)。

## 8. 次工程

| 工程 | 出力 |
|---|---|
| Design A/B/C 並列展開 | `PHASE_D_DESIGN_A/B/C.md` |
| Design 採用案 (Picked) | `PHASE_D_DESIGN_P.md` |
| Wave 分割 | `PHASE_D_WAVE_PLAN.md` |
| Issue 一覧 + 詳細 | `PHASE_D_ISSUES_INDEX.md` |
| 高位サマリ | `docs/PHASE_D_INDEX.md` |
| **GH Issue 起票** | label `phase:D` / `wave:WD0-WD5` / `area:api|webgis|winforms|db|tests|docs|infra` |
| WD0 PoC 着手 | `tools/poc/GeoServerCheck/` + go/no-go 判定 → `PHASE_D_D100_POC_RESULT.md` |

## 9. 関連メモリ

- `scale_target_and_server_side_rendering.md`: 採用方針の論拠
- `selection_visualization_and_multi_select.md`: 選択 raster オーバーレイ詳細
- `rendering_architecture_shift.md`: Phase A/B/C → Phase D 転換の全体俯瞰
- `architecture.md`: WinForms+WebView2+WebGIS+API+PostGIS ハイブリッド構成
- `orchestration_state.md`: Phase A/B/C 完了状態 + Phase D 着手ポイント
- `stacked_pr_pitfall.md`: `base=main` 固定ルール (Phase D も踏襲)
- `smart_app_control_pitfall.md`: WinForms ローカル smoke は Release 構成必須
