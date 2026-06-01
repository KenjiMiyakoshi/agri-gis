# Issue 番号対応表

`docs/issues/NNNN-*.md` の元番号と GitHub Issues 番号の対応表。

以後の作業（コーディング・テスト・PR）では **GitHub Issues 番号** を主に参照する。
本文中の `Depends on: 0108` のような表記は元番号のまま残してある（grep しやすさ優先）。

リポジトリ: <https://github.com/KenjiMiyakoshi/agri-gis>

## DB (phase/db)

| 元番号 | GitHub | Wave | タイトル |
|---|---|---|---|
| 0101 | [#1](https://github.com/KenjiMiyakoshi/agri-gis/issues/1) | W1 | `db/migration/` ディレクトリ整備とマイグレーション運用方針 |
| 0102 | [#2](https://github.com/KenjiMiyakoshi/agri-gis/issues/2) | W1 | `layers` テーブル拡張 (schema_json, schema_version) |
| 0103 | [#3](https://github.com/KenjiMiyakoshi/agri-gis/issues/3) | W1 | `feature_current` テーブル拡張 (created_by/updated_by/version/schema_version, TIMESTAMPTZ化) |
| 0104 | [#4](https://github.com/KenjiMiyakoshi/agri-gis/issues/4) | W1 | `feature_history` テーブル新設 |
| 0105 | [#5](https://github.com/KenjiMiyakoshi/agri-gis/issues/5) | W1 | `audit_log` テーブル新設 |
| 0106 | [#6](https://github.com/KenjiMiyakoshi/agri-gis/issues/6) | W1 | `layer_schema_version` テーブル新設 + 初日稼働シード |
| 0107 | [#7](https://github.com/KenjiMiyakoshi/agri-gis/issues/7) | W2 | `fn_feature_insert` 実装 |
| 0108 | [#8](https://github.com/KenjiMiyakoshi/agri-gis/issues/8) | W2 | `fn_feature_update` 実装 (楽観ロック) |
| 0109 | [#9](https://github.com/KenjiMiyakoshi/agri-gis/issues/9) | W2 | `fn_feature_delete` 実装 (履歴退避) |
| 0110 | [#10](https://github.com/KenjiMiyakoshi/agri-gis/issues/10) | W2 | `fn_layer_schema_upsert` 実装 |
| 0111 | [#11](https://github.com/KenjiMiyakoshi/agri-gis/issues/11) | W2 | 既存シード (`002_seed.sql`) の新スキーマ対応 |

## API (phase/api)

| 元番号 | GitHub | Wave | タイトル |
|---|---|---|---|
| 0201 | [#12](https://github.com/KenjiMiyakoshi/agri-gis/issues/12) | W1 | `MapGroup` 3 分割と `Endpoints/` 構造 |
| 0202 | [#13](https://github.com/KenjiMiyakoshi/agri-gis/issues/13) | W1 | DTO 定義 (record) と JSON 設定 |
| 0203 | [#14](https://github.com/KenjiMiyakoshi/agri-gis/issues/14) | W1 | `X-Actor` / `X-Request-Id` ミドルウェアとヘルパ |
| 0204 | [#15](https://github.com/KenjiMiyakoshi/agri-gis/issues/15) | W1 | ProblemDetails + errors[] 拡張と例外マッピング |
| 0205 | [#16](https://github.com/KenjiMiyakoshi/agri-gis/issues/16) | W2 | `GET /api/layers` 拡張 (schema_json 含める) |
| 0206 | [#17](https://github.com/KenjiMiyakoshi/agri-gis/issues/17) | W2 | `GET /api/layers/{layerId}/schema` 実装 |
| 0207 | [#18](https://github.com/KenjiMiyakoshi/agri-gis/issues/18) | W2 | `PUT /api/admin/layers/{layerId}/schema` 実装 |
| 0208 | [#19](https://github.com/KenjiMiyakoshi/agri-gis/issues/19) | W2 | `GET /api/features` 拡張 (asOf, UNION ALL) |
| 0209 | [#20](https://github.com/KenjiMiyakoshi/agri-gis/issues/20) | W2 | `GET /api/features/{entityId}` + `GET /api/features/{entityId}/history` |
| 0210 | [#21](https://github.com/KenjiMiyakoshi/agri-gis/issues/21) | W2 | `POST /api/features` 実装 |
| 0211 | [#22](https://github.com/KenjiMiyakoshi/agri-gis/issues/22) | W2 | `PATCH /api/features/{entityId}` 実装 (If-Match) |
| 0212 | [#23](https://github.com/KenjiMiyakoshi/agri-gis/issues/23) | W2 | `DELETE /api/features/{entityId}` 実装 |

## Test (phase/test)

| 元番号 | GitHub | Wave | タイトル |
|---|---|---|---|
| 0301 | [#24](https://github.com/KenjiMiyakoshi/agri-gis/issues/24) | W3 | `AgriGis.Api.Tests` プロジェクト立ち上げ + Testcontainers |
| 0302 | [#25](https://github.com/KenjiMiyakoshi/agri-gis/issues/25) | W3 | テストフィクスチャ (TRUNCATE/seed) と共通ヘルパ |
| 0303 | [#26](https://github.com/KenjiMiyakoshi/agri-gis/issues/26) | W3 | 不変条件テスト (INSERT/UPDATE/DELETE) |
| 0304 | [#27](https://github.com/KenjiMiyakoshi/agri-gis/issues/27) | W3 | 楽観ロック / スキーマ違反 / asOf / X-Actor テスト |
| 0305 | [#28](https://github.com/KenjiMiyakoshi/agri-gis/issues/28) | W3 | `docs/testing-policy.md` 執筆 |

## WebGIS (phase/webgis)

| 元番号 | GitHub | Wave | タイトル |
|---|---|---|---|
| 0401 | [#29](https://github.com/KenjiMiyakoshi/agri-gis/issues/29) | W1 | `webgis/src/` モジュール分割 |
| 0402 | [#30](https://github.com/KenjiMiyakoshi/agri-gis/issues/30) | W2 | WebGIS API クライアントと型定義 (DTO 命名一致) |
| 0403 | [#31](https://github.com/KenjiMiyakoshi/agri-gis/issues/31) | W2 | WebView2 bridge + メッセージ envelope + requestId 重複検知 |
| 0404 | [#32](https://github.com/KenjiMiyakoshi/agri-gis/issues/32) | W3 | Vitest 最小セットアップ + envelope / 重複検知テスト |

## WinForms (phase/winforms)

| 元番号 | GitHub | Wave | タイトル |
|---|---|---|---|
| 0501 | [#33](https://github.com/KenjiMiyakoshi/agri-gis/issues/33) | W1 | `AgriGis.Desktop.csproj` 新規作成と依存導入 |
| 0502 | [#34](https://github.com/KenjiMiyakoshi/agri-gis/issues/34) | W2 | WinForms `Core/` 純粋ロジック |
| 0503 | [#35](https://github.com/KenjiMiyakoshi/agri-gis/issues/35) | W2 | WinForms `Services/` (IApiClient/ApiClient, IBridgeMessenger/BridgeMessenger) |
| 0504 | [#36](https://github.com/KenjiMiyakoshi/agri-gis/issues/36) | W3 | `Forms/MainForm` + `AttributeEditorControl` + WebView2 初期化 |
| 0505 | [#37](https://github.com/KenjiMiyakoshi/agri-gis/issues/37) | W3 | `AgriGis.Desktop.Tests.csproj` + Core 単体 + ConventionTest |

## Docs (phase/docs)

| 元番号 | GitHub | Wave | タイトル |
|---|---|---|---|
| 0601 | [#38](https://github.com/KenjiMiyakoshi/agri-gis/issues/38) | W4 | メッセージ規約ドキュメント (envelope, 5 タイプ, 将来追加) |
| 0602 | [#39](https://github.com/KenjiMiyakoshi/agri-gis/issues/39) | W4 | `README.md` 更新 (新アーキ、起動手順、Desktop) |

## Wave別サマリ

| Wave | 対象イシュー | 件数 |
|---|---|---|
| Wave1: 基盤 | #1-#6, #12-#15, #29, #33 | 12 |
| Wave2: コア | #7-#11, #16-#23, #30-#31, #34-#35 | 17 |
| Wave3: 統合 | #24-#28, #32, #36-#37 | 8 |
| Wave4: 仕上げ | #38-#39 | 2 |
| **合計** | | **39** |
