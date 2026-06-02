# Phase D Index — 描画アーキ転換 (クライアントベクタ → サーバラスタタイル)

agri-gis Phase D (`描画アーキ転換`) サイクルの高位サマリ。Phase A (認証基盤) / Phase B (レイヤ管理 + GeoJSON/CSV インポート) / Phase C (Shapefile + GDAL インポート) 完了後の次サイクル。

## スコープ

`GET /api/features?layerId=` で全 feature を GeoJSON で返してクライアント (OpenLayers) でベクタ描画する現状を、**GeoServer 同梱によるサーバラスタタイル方式** (`GET /tiles/{layerId}/{theme}/{z}/{x}/{y}.png`) に転換する。

数百万件規模の運用、頻繁なスタイル変更、選択ハイライト raster overlay を本要件として吸収。

## 採用方針

| 観点 | 採用 |
|---|---|
| 描画エンジン | **GeoServer 2.25.x 同梱** (Docker Compose) |
| キャッシュ層 | GeoServer 内部キャッシュ (Phase D)、MapProxy は Phase D' 申し送り |
| theme 保管 | DB `layers.style_json JSONB` 列、admin API で CRUD |
| 選択 sid TTL | セッション終了まで + 発行ユーザのみ取得可能 |
| 本番 GeoServer | 別ホスト前提 (k8s/VM)、dev のみ docker-compose 同梱 |
| `?layerId=` API | WD3 完了時点で **410 Gone** |
| MVT (ベクタタイル) | 不採用 (本要件と不一致) |

詳細は `docs/issues/PHASE_D_DESIGN_P.md`。

## Wave 構成

| Wave | テーマ | 工数 | Issue |
|---|---|---|---|
| **WD0** | PoC スパイク (Gate) | 0.5-1.0d | [D100](issues/PHASE_D_ISSUES_INDEX.md#d100) |
| **WD1** | dev インフラ + DB migration | 2.0d | D101, D102, D103 |
| **WD2** | API 新設 + Sunset | 2.5d | D201, D202, D203, D204, D205 |
| **WD3** | WebGIS raster 経路 | 2.0d | D301, D302, D303 |
| **WD4** | WinForms bridge | 1.0d | D401, D402 |
| **WD5** | テスト + ドキュメント | 3.0d | D501-D504, D601, D602 |
| | **合計** | **約 11.0-11.5d** | **20 Issue** |

クリティカルパス約 9-10 営業日 + バッファ。Phase B (11.5d) と同等規模、Phase C (8-9 営業日) よりリスク高。

詳細は `docs/issues/PHASE_D_WAVE_PLAN.md`。

## 主要 API 変更

| Method | Path | 状態 |
|---|---|---|
| GET | `/tiles/{layerId}/{theme}/{z}/{x}/{y}.png` | 新規 (D201) |
| POST | `/api/selection` | 新規 (D202) |
| GET | `/tiles/selection/{sid}/{z}/{x}/{y}.png` | 新規 (D202) |
| DELETE | `/api/selection/{sid}` | 新規 (D202) |
| GET | `/api/admin/layers/{id}/style` | 新規 (D203) |
| PUT | `/api/admin/layers/{id}/style` | 新規 (D203) |
| POST | `/api/auth/logout` | 新規 (D204) |
| GET | `/api/features?layerId=` | **WD3 末で 410 Gone (D303)** |

## DB マイグレーション

| # | ファイル | 内容 |
|---|---|---|
| 0D01 | `0D01_layers_style_json.sql` | `layers.style_json JSONB NOT NULL DEFAULT '{}'` |
| 0D02 | `0D02_user_sessions.sql` | JWT lifecycle 管理テーブル |
| 0D03 | `0D03_selection_sets.sql` | 選択集合の一時保存テーブル |
| 0D04 | `0D04_selection_sets_session_link.sql` | sid → session_id FK CASCADE |

## 受け入れ条件 (Phase D 完了の定義)

1. dev `docker-compose up -d` で geoserver サービスが起動、`/geoserver/web/` が HTTP 200
2. WebGIS で `?layerId=` 経由のベクタ読み込みが 410 Gone、代わりに TileLayer で図形が表示される
3. クリック選択 → `POST /api/selection` → 選択 overlay TileLayer が表示される
4. 50 万件 fixture で z=15 タイル平均応答時間 < 500ms
5. `windos-app.tests` (推定 120 件) + `api.tests` (推定 90 件) + `webgis vitest` 全 green
6. `docs/rendering.md` + `docs/deploy/geoserver-prod.md` 作成
7. PR 単位で全 6 Wave (WD0-WD5) が main にマージ済
8. `orchestration_state.md` メモリ更新

## PR 一覧

| Wave | PR | 状態 |
|---|---|---|
| Design 8 本 + WD0 PoC scaffold | [#168](https://github.com/KenjiMiyakoshi/agri-gis/pull/168) | マージ済 (Design + scaffolding) |
| WD0 PoC 実行 | (PoC は `tools/poc/GeoServerCheck/`、Issue #169 でクローズ予定) | (Docker 起動済、verify.sh は朝に手動実行) |
| WD1 (`feature/phase-d-wd1-infra`) | [#189](https://github.com/KenjiMiyakoshi/agri-gis/pull/189) | レビュー待ち (D101+D102+D103) |
| WD2 (`feature/phase-d-wd2-api`) | [#190](https://github.com/KenjiMiyakoshi/agri-gis/pull/190) | レビュー待ち (D201-D205) |
| WD3 (`feature/phase-d-wd3-webgis`) | [#191](https://github.com/KenjiMiyakoshi/agri-gis/pull/191) | レビュー待ち (D301-D303 + ?layerId= 410) |
| WD4 (`feature/phase-d-wd4-winforms`) | [#192](https://github.com/KenjiMiyakoshi/agri-gis/pull/192) | レビュー待ち (D401+D402) |
| WD5 (`feature/phase-d-wd5-tests-docs`) | (本 PR) | レビュー待ち (D504 + D601 + D602 + 部分 D501-D503) |

マージ順: #168 (済) → #189 → #190 → #191 → #192 → WD5 PR。各 PR `base=main` 固定 (`stacked_pr_pitfall` memory)。

## テスト総数 (Phase D 完了時想定)

- `api.tests`: 60 → **64** (D504 で 3 件 Skip 解除 + 410 確認 1 件追加)
- `webgis vitest`: 7 → **9** (D303 で envelope 3 種ラウンドトリップ追加)
- `windos-app.tests`: 118 (Phase D は WD5 D503 で +2 件予定だが本 WD5 PR では未着手)
- **計 191** (Phase C 完了時 178 → +13)

WD5 で WireMock 等を使った API 統合テスト (TilesProxy/Selection/AdminLayerStyle/AuthLogout 計 4 系列) を本格実装するのは Phase D 完了後の追加 PR で扱う案。本 WD5 PR は最低限の Skip 解除 + docs に集中。

## Phase D' 申し送り

- MapProxy 永続キャッシュ層 (タイル cache の永続化)
- カスタム theme 編集 Web UI (Phase D は admin API のみ)
- `POST /api/features/batch-update` 一括属性編集
- 編集→タイル無効化の WebSocket 通知 (楽観的更新 UX)
- カラーランプ生成 UI (SLD パターン集の Web 編集)
- 本番 GeoServer の k8s 自動化 (Helm chart)
- AttributeEditor N 件編集モード (Phase D は属性閲覧のみ disable)

## 関連ドキュメント

- `PHASE_A_INDEX.md` (Phase A 完了)
- `PHASE_B_INDEX.md` (Phase B 完了)
- `PHASE_C_INDEX.md` (Phase C 完了)
- `docs/issues/PHASE_D_PLAN.md`
- `docs/issues/PHASE_D_DESIGN_A.md`
- `docs/issues/PHASE_D_DESIGN_B.md`
- `docs/issues/PHASE_D_DESIGN_C.md`
- `docs/issues/PHASE_D_DESIGN_P.md`
- `docs/issues/PHASE_D_WAVE_PLAN.md`
- `docs/issues/PHASE_D_ISSUES_INDEX.md`

## 関連メモリ

- `scale_target_and_server_side_rendering.md`
- `selection_visualization_and_multi_select.md`
- `rendering_architecture_shift.md`
- `architecture.md`
- `orchestration_state.md`
- `stacked_pr_pitfall.md`
- `smart_app_control_pitfall.md`
