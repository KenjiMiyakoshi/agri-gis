# agri-gis Phase D Wave 分割計画 (案 P)

`PHASE_D_ISSUES_INDEX.md` の 20 Issue を、Phase A WA1〜WA5 / Phase B WB0〜WB5 / Phase C WC0〜WC4 と同じ流儀で Wave 分割。Phase C より 1 Wave 多い 6 Wave 構成 (WD0〜WD5)、Phase B と同等の 11.5 人日。

## 0. 運用前提 (Phase A/B/C 踏襲)

- **branch-per-Wave + base=main 固定**: 各 Wave で `feature/phase-d-wd{N}-{slug}` ブランチを切る。Issue ごとの PR ではなく **Wave 単位の集約 PR を main へ `--no-ff` でマージ**。Phase A/B/C と同流儀
- **stacked PR pitfall 回避** (MEMORY.md `stacked_pr_pitfall`): Wave PR は常に `base=main`。前 Wave がマージ済の main から fresh に切り出して開始。`base=feature/...` は禁止
- **Wave 内 Issue 順**: Issue 番号順を推奨着手順とし、依存のない Issue は並列実装可能
- **Wave 内コミット**: Issue ID prefix (`D101:`, `D201:` 等)
- **Wave 完了 = main マージ + Wave 検証手順クリア**: 全テスト green + 受け入れ条件サマリを PR description に転記
- **失敗時のロールバック**: Wave PR を revert すれば main に戻せる粒度を維持。Wave 内に巨大 Issue を入れない
- **ラベル**: Wave 単位で `wave:WD0`〜`wave:WD5`、`phase:D`、`area:db|api|webgis|winforms|tests|docs|infra` を併用
- **WinForms ローカル smoke**: `smart_app_control_pitfall` メモリに従い `-c Release` で起動 (Phase D 中も継続)

## 1. Wave 一覧

| Wave | テーマ | 含む Issue | 工数 | 前提依存 | 並列実行可否 |
|---|---|---|---|---|---|
| **WD0** | PoC スパイク (Gate) | D100 | 0.5-1.0d | なし | 単独 (Gate) |
| **WD1** | dev インフラ + DB migration | D101, D102, D103 | 2.0d | WD0 go | D101 (compose) → D102/D103 (migration) は並列可 |
| **WD2** | API 新設 + Sunset | D201, D202, D203, D204, D205 | 2.5d | WD1 完了 | D201/D202/D203/D204 並列、D205 は最後 |
| **WD3** | WebGIS raster 経路 | D301, D302, D303 | 2.0d | WD2 完了 (tile / selection endpoints 利用) | D301 → D302 → D303 直列 |
| **WD4** | WinForms bridge | D401, D402 | 1.0d | WD3 完了 (envelope 確定後) | D401 → D402 直列 |
| **WD5** | テスト + ドキュメント | D501, D502, D503, D504, D601, D602 | 3.0d | WD4 完了 | D501/D502/D503 並列、D504/D601/D602 並列 |
| | **合計** | **20 Issue** | **11.0-11.5d** | | |

クリティカルパス: WD0 (0.5d) → WD1 (1.5d 直列分) → WD2 (2.5d、並列度高で短縮可) → WD3 (2.0d 直列) → WD4 (1.0d) → WD5 (1.5d 直列分) ≒ **9-10 営業日 + バッファ**。並列度を最大化すれば 8 営業日も視野。

---

## 2. Wave 詳細

### WD0 — GeoServer 同梱 PoC (Gate)

- **テーマ**: Phase D 全体の着手前提条件として GeoServer + PostgreSQL JNDI + SLD パラメタライズ + CQL_FILTER 選択 raster の 3 要件を実機 PoC で確認する。`tools/poc/GeoServerCheck/` に最小構成を置き、`docker-compose -f tools/poc/GeoServerCheck/docker-compose.yml up` で動作確認
- **含む Issue**: D100
- **工数**: 0.5-1.0d
- **依存**: なし
- **並列**: 単独 (Gate Wave、後続全 Wave がブロックされる)
- **ブランチ**: `feature/phase-d-wd0-poc`
- **マージ順**: 1 番目
- **検証手順**:
  1. `tools/poc/GeoServerCheck/` 配下 `docker-compose up -d` で geoserver サービスが起動 (60-90 秒待機)
  2. `curl -u admin:geoserver http://localhost:8888/geoserver/web/` が HTTP 200
  3. `agrigis` workspace 作成 → `postgis_jndi` datastore 接続 → `feature_current` テーブルを WMS layer 公開
  4. `feature_current` に 100 件サンプル INSERT 後、`GET /geoserver/agrigis/wms?...&z=15&x=...&y=...&FORMAT=image/png&STYLES=default` で PNG 200 取得
  5. SLD 2 種 (`default.sld`, `byOwner.sld`) で同 z/x/y の PNG が配色変化を確認
  6. `selection_sets_poc` 一時テーブル + `CQL_FILTER=entity_id IN (...)` で 1000 件選択の透過 PNG 取得 + 応答時間計測
  7. `docs/issues/PHASE_D_D100_POC_RESULT.md` に出力 PNG パス・応答時間・go/no-go 判定を記録
  8. `no-go` の場合は Full Compose 化を一旦保留し原因切り分け
  9. `?layerId=` 依存テスト件数を `grep -r "GET /api/features?layerId" api.tests/` で計上、PR description に記録

### WD1 — dev インフラ + DB migration

- **テーマ**: dev `docker-compose.yml` に geoserver サービスを追加 + `layers.style_json` / `user_sessions` / `selection_sets` の 4 本 DB migration + JWT 発行/検証経路に `session_id` claim を追加
- **含む Issue**: D101 (Docker Compose 拡張, 0.5d), D102 (migration 4 本 + bootstrap, 1.0d), D103 (JWT に session_id claim, 0.5d)
- **工数**: 2.0d
- **依存**: WD0 (D100 go 判定)
- **並列**: Wave 内 Issue 間は **D101 → (D102 + D103) 並列**。D102 (migration) と D103 (JWT) は依存なし
- **ブランチ**: `feature/phase-d-wd1-infra`
- **マージ順**: 2 番目
- **検証手順**:
  1. `docker-compose up -d` で `postgis` + `geoserver` 両サービスが healthy
  2. `psql` で `\d layers` / `\d selection_sets` / `\d user_sessions` で新列・新テーブル確認
  3. `dotnet build` 成功、`AGRI_GIS_JWT_SECRET` を環境変数で渡し起動して既存ログイン成功
  4. 新規 JWT に `sid_session` claim が含まれることを `jwt.io` で確認
  5. `api.tests` 既存全 green 維持 (`session_id` claim 追加は old token 拒否を許容するため `--no-token` テストの再生成は WD5 で吸収)
  6. `geoserver/data_dir/` 初期 workspace + datastore + 2 SLD を git 管理

### WD2 — API 新設 + Sunset

- **テーマ**: tile proxy / selection / theme CRUD / logout / `?layerId=` Sunset の 5 endpoint 新設 + 既存 `?layerId=` 経路に Sunset ヘッダ
- **含む Issue**: D201 (tile proxy, 0.5d), D202 (selection + sid lifecycle, 0.8d), D203 (admin theme CRUD + GeoServer 同期, 0.6d), D204 (logout endpoint, 0.3d), D205 (`?layerId=` Sunset + IApiClient cleanup, 0.3d)
- **工数**: 2.5d
- **依存**: WD1 完了
- **並列**: D201/D202/D203/D204 並列可、D205 は最後 (`?layerId=` Sunset は migration 影響範囲を踏まえ最終)
- **ブランチ**: `feature/phase-d-wd2-api`
- **マージ順**: 3 番目
- **検証手順**:
  1. `dotnet test api.tests` 全 green (新規テストは WD5 で追加、WD2 は既存テスト保護のみ)
  2. WinForms から `POST /api/selection` を curl で叩き sid 取得 → 別 user の JWT で `GET /tiles/selection/{sid}/...` が 403
  3. `GET /tiles/15/default/30000/12000.png` が PNG 200 (GeoServer モック or 実機経由)
  4. `PUT /api/admin/layers/{id}/style` で SLD 更新後、`GET /api/admin/layers/{id}/style` で同 JSON 取得
  5. `GET /api/features?layerId=1` が `Sunset: <date>` + `Deprecation: true` ヘッダを返す
  6. `IApiClient.GetFeaturesAsync` がコードベースから削除済

### WD3 — WebGIS raster 経路

- **テーマ**: OL `VectorSource` → `TileLayer` 主役切替 + 選択 2 段パイプライン + bridge envelope 拡張
- **含む Issue**: D301 (TileLayer 化 + theme 切替, 0.8d), D302 (selection 2 段, 0.7d), D303 (bridge envelope 拡張, 0.5d)
- **工数**: 2.0d
- **依存**: WD2 (tile / selection endpoints 利用)
- **並列**: D301 → D302 → D303 **直列** (TileLayer 基盤を D301 が用意、選択 overlay は D302 でその上に乗せる、envelope は D303 で確定)
- **ブランチ**: `feature/phase-d-wd3-webgis`
- **マージ順**: 4 番目
- **検証手順**:
  1. `cd webgis && pnpm dev` で WebGIS 起動、地図に既存 vector ではなく TileLayer が表示される
  2. クリック → `POST /api/selection` → 選択 overlay TileLayer が薄黄色で重なる
  3. WinForms から `theme_change` envelope を投げると地図の配色が変わる
  4. WD3 末で API `?layerId=` を **410 Gone** に切り替え (一行修正)
  5. `vectorSource` 関連コードが webgis から削除済
  6. vitest 既存 green (新規テストは WD5)

### WD4 — WinForms bridge

- **テーマ**: bridge handler を `feature_clicked` 単数 → `features_selected` 配列に切替 + AttributeEditor 単数/N 件モード + ApiClient メソッド差替
- **含む Issue**: D401 (MainForm bridge + ApiClient 差替, 0.6d), D402 (AttributeEditor N 件モード, 0.4d)
- **工数**: 1.0d
- **依存**: WD3 完了 (envelope 確定後)
- **並列**: D401 → D402 直列
- **ブランチ**: `feature/phase-d-wd4-winforms`
- **マージ順**: 5 番目
- **検証手順**:
  1. `dotnet run -c Release` で起動 (SAC 回避)、login → MainForm 表示
  2. WebGIS で 1 件クリック → AttributeEditor が単数モードで属性表示
  3. WebGIS で複数件ドラッグ選択 (D302 で実装) → AttributeEditor が N 件モードで件数表示・編集 disable
  4. theme 切替 ComboBox 操作で WebGIS の配色変化
  5. logout ボタンで `POST /api/auth/logout` → `selection_sets` が cascade 削除されることを別途 psql で確認

### WD5 — テスト + ドキュメント

- **テーマ**: 新規 API/WebGIS/WinForms テスト + `?layerId=` 依存テスト書き換え + 性能 smoke + docs 2 本
- **含む Issue**: D501 (api.tests 新規 4 件, 0.8d), D502 (webgis vitest 新規 2 件, 0.4d), D503 (windos-app.tests 新規 2 件, 0.4d), D504 (`?layerId=` 書き換え + 性能 smoke, 0.8d), D601 (`docs/rendering.md`, 0.3d), D602 (`docs/deploy/geoserver-prod.md` + PHASE_D_INDEX.md 仕上げ, 0.3d)
- **工数**: 3.0d
- **依存**: WD4 完了
- **並列**: D501/D502/D503 並列 → D504 (テスト書き換え + 性能) → D601/D602 並列
- **ブランチ**: `feature/phase-d-wd5-tests-docs`
- **マージ順**: 6 番目
- **検証手順**:
  1. `dotnet test api.tests` + `pnpm test webgis` + `dotnet test windos-app.tests -c Release` 全 green
  2. 50 万件 fixture (Phase C `sample-shp-generator` を 1000 倍化) で `GET /tiles/...` を 5 リクエスト平均 < 500ms (cold cache)
  3. `docs/rendering.md` (Phase D アーキ解説) + `docs/deploy/geoserver-prod.md` (本番別ホスト構成手順)
  4. `docs/PHASE_D_INDEX.md` 最終化 (Wave PR 一覧 + 受け入れ条件チェックリスト)
  5. Phase D 完了報告と `orchestration_state.md` 更新

---

## 3. クリティカルパスと並列度

```
WD0 (Gate) ─ D100 ──┬─ WD1 ─ D101 ─┬─ D102 ─┐
                    │              └─ D103 ─┤
                    └─ WD2 ─ D201/D202/D203/D204 並列 ─ D205 ─┐
                                                              ├─ WD3 ─ D301 ─ D302 ─ D303 ─┐
                                                              │                            ├─ WD4 ─ D401 ─ D402 ─┐
                                                              │                            │                    │
                                                              │                            └────────────────────┤
                                                              │                                                 │
                                                              └─────────────────────────────────────────────────┴─ WD5 ─ D501/D502/D503 並列 ─ D504 ─ D601/D602 並列
```

並列実装可能な単位:
- WD1 内: D102 / D103 は完全独立
- WD2 内: D201/D202/D203/D204 が独立
- WD5 内: D501/D502/D503 が独立、D601/D602 が独立

並列を最大化した場合のクリティカルパス: WD0 (1d) + WD1 (1d) + WD2 (1.2d) + WD3 (2d) + WD4 (1d) + WD5 (1.5d) ≒ **7.7d**。

実装者が 1 人想定なら約 11d、2 人並列なら約 8d、3 人並列なら約 7d。

## 4. 既知の制約と申し送り

- **CI 上で docker-compose を起動しない方針**: api.tests は GeoServer モックのみ。CI smoke は API + DB のみ実行。docker-compose 起動を含む E2E smoke は手動 (PR description に手順)
- **JWT 互換性破壊**: WD1 D103 で JWT に `sid_session` claim を追加するため、既発行 token は全て無効化される。WD5 docs にデプロイ手順 (全ユーザ再ログイン) を明記
- **本番 GeoServer デプロイは Phase D 外**: WD5 D602 で手順だけ書き、実セットアップは別タスク
- **MapProxy 導入は Phase D' 申し送り**: `selection_visualization_and_multi_select.md` メモリと整合
- **編集 → タイル無効化の楽観的 UX**: Phase D は手動リロード前提。WebSocket 通知は Phase D'

## 5. 関連ドキュメント

- `PHASE_D_PLAN.md`: Plan 工程
- `PHASE_D_DESIGN_P.md`: 採用案
- `PHASE_D_ISSUES_INDEX.md`: Issue 一覧 + 詳細
- `docs/PHASE_D_INDEX.md`: 高位サマリ
