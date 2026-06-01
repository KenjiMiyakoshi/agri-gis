# agri-gis イシュー一覧

採択案「案B'」を後続のコーディングフェーズで処理可能な単位に分割したもの。
1 イシュー = 半日〜1日（5〜10時間相当）を目安にしている。

## フェーズ別の番号体系

| 番号帯 | フェーズ | 概要 |
|---|---|---|
| 01xx | DB | スキーマ改修、追加テーブル、PL/pgSQL 関数、マイグレーション |
| 02xx | API | DTO、エンドポイント、ProblemDetails、X-Actor、楽観ロック |
| 03xx | Test | API.Tests、Testcontainers、不変条件テスト、testing-policy |
| 04xx | WebGIS | モジュール分割、bridge、メッセージ envelope、Vitest |
| 05xx | WinForms | Desktop プロジェクト、Core/Services/Forms、ConventionTest |
| 06xx | 統合・ドキュメント | README、起動手順、メッセージ規約 |

## イシュー一覧

| 番号 | フェーズ | タイトル | 工数 | 依存 |
|---|---|---|---|---|
| 0101 | DB | `db/migration/` ディレクトリ整備とマイグレーション運用方針 | 0.5d | なし |
| 0102 | DB | `layers` テーブル拡張 (schema_json, schema_version) | 0.5d | 0101 |
| 0103 | DB | `feature_current` テーブル拡張 (created_by/updated_by/version/schema_version, TIMESTAMPTZ化) | 0.5d | 0101 |
| 0104 | DB | `feature_history` テーブル新設 | 0.5d | 0103 |
| 0105 | DB | `audit_log` テーブル新設 | 0.5d | 0101 |
| 0106 | DB | `layer_schema_version` テーブル新設 + 初日稼働シード | 0.5d | 0102 |
| 0107 | DB | `fn_feature_insert` 実装 | 1d | 0103, 0105 |
| 0108 | DB | `fn_feature_update` 実装 (楽観ロック) | 1d | 0104, 0105, 0107 |
| 0109 | DB | `fn_feature_delete` 実装 (履歴退避) | 0.5d | 0104, 0105 |
| 0110 | DB | `fn_layer_schema_upsert` 実装 | 0.5d | 0102, 0106 |
| 0111 | DB | 既存シード (`002_seed.sql`) の新スキーマ対応 | 0.5d | 0102, 0103, 0106 |
| 0201 | API | `MapGroup` 3 分割と `Endpoints/` 構造 | 0.5d | なし |
| 0202 | API | DTO 定義 (record) と JSON 設定 | 0.5d | 0201 |
| 0203 | API | `X-Actor` / `X-Request-Id` ミドルウェアとヘルパ | 0.5d | 0201 |
| 0204 | API | ProblemDetails + errors[] 拡張と例外マッピング | 0.5d | 0203 |
| 0205 | API | `GET /api/layers` 拡張 (schema_json 含める) | 0.5d | 0202, 0102 |
| 0206 | API | `GET /api/layers/{layerId}/schema` 実装 | 0.5d | 0205 |
| 0207 | API | `PUT /api/admin/layers/{layerId}/schema` 実装 | 0.5d | 0206, 0110 |
| 0208 | API | `GET /api/features` 拡張 (asOf, UNION ALL) | 1d | 0202, 0103, 0104 |
| 0209 | API | `GET /api/features/{entityId}` + `GET /api/features/{entityId}/history` | 0.5d | 0208 |
| 0210 | API | `POST /api/features` 実装 | 0.5d | 0204, 0107 |
| 0211 | API | `PATCH /api/features/{entityId}` 実装 (If-Match) | 1d | 0204, 0108 |
| 0212 | API | `DELETE /api/features/{entityId}` 実装 | 0.5d | 0204, 0109 |
| 0301 | Test | `AgriGis.Api.Tests` プロジェクト立ち上げ + Testcontainers | 1d | 0101 |
| 0302 | Test | テストフィクスチャ (TRUNCATE/seed) と共通ヘルパ | 0.5d | 0301 |
| 0303 | Test | 不変条件テスト (INSERT/UPDATE/DELETE) | 1d | 0302, 0210, 0211, 0212 |
| 0304 | Test | 楽観ロック / スキーマ違反 / asOf / X-Actor テスト | 1d | 0303 |
| 0305 | Test | `docs/testing-policy.md` 執筆 | 0.5d | 0301 |
| 0401 | WebGIS | `webgis/src/` モジュール分割 (map/api/bridge/controllers) | 1d | なし |
| 0402 | WebGIS | API クライアントと型定義 (DTO 命名一致) | 0.5d | 0401, 0202 |
| 0403 | WebGIS | WebView2 bridge + メッセージ envelope + requestId 重複検知 | 1d | 0401 |
| 0404 | WebGIS | Vitest 最小セットアップ + envelope / 重複検知テスト | 0.5d | 0403 |
| 0501 | WinForms | `AgriGis.Desktop.csproj` 新規作成と依存導入 | 0.5d | なし |
| 0502 | WinForms | `Core/` 純粋ロジック (AttributeValidator, SchemaFormBuilder, ProblemDetailsParser, ActorContext, LayerSchema) | 1d | 0501 |
| 0503 | WinForms | `Services/` (IApiClient/ApiClient, IBridgeMessenger/BridgeMessenger) | 1d | 0501, 0502 |
| 0504 | WinForms | `Forms/MainForm` + `AttributeEditorControl` + WebView2 初期化 | 1d | 0503, 0403 |
| 0505 | WinForms | `AgriGis.Desktop.Tests.csproj` + Core 単体 + ConventionTest | 1d | 0502 |
| 0601 | Docs | メッセージ規約ドキュメント (envelope, 5 タイプ, 将来追加) | 0.5d | 0403 |
| 0602 | Docs | `README.md` 更新 (新アーキ、起動手順、Desktop) | 0.5d | 0211, 0504 |

## 工数合計目安

- DB (01xx): 6.5d
- API (02xx): 7.0d
- Test (03xx): 4.0d
- WebGIS (04xx): 3.0d
- WinForms (05xx): 4.5d
- Docs (06xx): 1.0d

**合計: 約 26.0d** (1 人換算)
