# agri-gis Phase A イシュー一覧 (認証基盤)

採択案「案 P」(Phase A: 認証基盤) を半日〜1 日粒度に分割したもの。
W1-W4 の `0101-0602` と区別するため、Phase A は `A1xx-A6xx` 番号体系を採用する。

## フェーズ別の番号体系

| 番号帯 | フェーズ | 概要 |
|---|---|---|
| A1xx | DB | 認証用テーブル、audit_log 拡張、PL/pgSQL 引数拡張、C1/C2/H1 修復 |
| A2xx | API | JwtBearer、ICurrentUser、ProblemDetails 401/403、auth エンドポイント、認可配置、初期 admin seed |
| A3xx | AdminCrud | /api/admin/organizations, /api/admin/users CRUD |
| A4xx | WinForms | ActorContext 削除、LoginForm、BearerHandler、401 再ログイン |
| A5xx | Test | SeedUsers、TokenForge、既存テスト整理、Auth/Admin/Audit/Bitemporal 系新規 |
| A6xx | Docs | docs/auth.md、README 更新 |

## イシュー一覧

| 番号 | フェーズ | タイトル | 工数 | 依存 |
|---|---|---|---|---|
| A101 | DB | organizations / users / user_roles DDL | 1d | なし |
| A102 | DB | audit_log に actor_user_id 追加 + actor を display_name 化 | 0.5d | A101 |
| A103 | DB | feature_current(entity_id) UNIQUE INDEX 追加 (H1 修復) | 0.5d | なし |
| A104 | DB | C1 修復 - fn_feature_update の valid_from/valid_to を CURRENT_DATE で接合 | 0.5d | A103 |
| A105 | DB | C2 修復 - 4 関数の to_jsonb から geom を抜き geom_geojson を追加 | 1d | A103 |
| A106 | DB | PL/pgSQL 関数引数の非破壊拡張 (p_user_id, p_org_id) | 1d | A101, A102, A104, A105 |
| A201 | API | BCrypt.Net-Next + JwtBearer + appsettings/環境変数 | 1d | A101 |
| A202 | API | ICurrentUser interface + HttpContextCurrentUser | 0.5d | A201 |
| A203 | API | IAuthorizationMiddlewareResultHandler で 401/403 を ProblemDetails 統合 | 0.5d | A201 |
| A204 | API | RequestContext から RequireActor 廃止 + middleware 順序見直し | 0.5d | A202, A203 |
| A205 | API | POST /api/auth/login + GET /api/auth/me + POST /api/auth/change-password | 1d | A201, A202, A204 |
| A206 | API | 既存エンドポイントへの [Authorize] / [AllowAnonymous] 配置 | 1d | A201, A202, A203, A204 |
| A207 | API | 初期 admin の IHostedService による upsert | 0.5d | A101, A201 |
| A301 | AdminCrud | /api/admin/organizations CRUD | 1d | A101, A202, A206 |
| A302 | AdminCrud | /api/admin/users CRUD + パスワード変更分離 | 1d | A101, A202, A206, A301 |
| A401 | WinForms | ActorContext 削除 + ISessionStore / InMemorySessionStore 新設 | 0.5d | なし |
| A402 | WinForms | LoginForm + Designer + 起動フロー | 1d | A205, A401, A403 |
| A403 | WinForms | BearerHandler + ApiClient.LoginAsync + X-Actor 削除 | 0.5d | A205, A401 |
| A404 | WinForms | 401 再ログインフロー + Guest UI 制限 | 0.5d | A401, A402, A403 |
| A501 | Test | SeedUsers fixture + DbReset で users/orgs seed | 0.5d | A101, A207 |
| A502 | Test | TokenForge + ApiClientFactory.WithActorAs 明示形 | 0.5d | A201, A501 |
| A503 | Test | 既存 AsOfTests の手動 UPDATE 削除 + MissingActorTests → AuthRequiredTests | 0.5d | A104, A204, A502 |
| A504 | Test | AuthLoginTests + JwtValidationTests + AnonymousReadTests | 1d | A205, A501, A502 |
| A505 | Test | AuthorizationTests (3 role × エンドポイント matrix) | 1d | A206, A502 |
| A506 | Test | AdminUsersCrudTests + AdminOrgsCrudTests | 1d | A301, A302, A502 |
| A507 | Test | AuditUserIdTests + AuditLogGeomStripTests (C2 回帰) | 0.5d | A102, A105, A106, A502 |
| A508 | Test | C1RegressionTests + BcryptHashTests + InitialAdminSeedTests | 1d | A104, A201, A207, A502 |
| A601 | Docs | docs/auth.md (JWT claims, role matrix, 鍵管理) | 0.5d | A201, A205, A206 |
| A602 | Docs | README 更新 (起動手順に LoginForm + 環境変数) | 0.5d | A207, A402, A601 |

## 総工数見積

| フェーズ | 件数 | 工数合計 |
|---|---|---|
| A1xx DB         | 6  | 4.5d |
| A2xx API        | 7  | 5.0d |
| A3xx AdminCrud  | 2  | 2.0d |
| A4xx WinForms   | 4  | 2.5d |
| A5xx Test       | 8  | 6.0d |
| A6xx Docs       | 2  | 1.0d |
| **合計**        | **29** | **21.0d** |

※ 並列化なしの単純合計。実際は DB → API → AdminCrud / WinForms / Test が並走可能。

## 依存関係（クリティカルパス概略）

```
A101 ──┬─ A102 ──┐
       ├─ A201 ──┬── A202 ──┬── A204 ──┬── A205 ──┬── A402 ──┬── A404
       │         ├── A203 ──┘          ├── A206 ──┤          │
       │         └─────────── A207 ────┤          ├── A403 ──┤
       │                                ├── A301 ─┴── A302
A103 ──┼── A104 ──┐
       └── A105 ──┴── A106
A401 ──── A402, A403, A404
A501 ──── A502 ──── A503, A504, A505, A506, A507, A508
A601, A602 (末尾)
```

主要パス: A101 → A201 → A202 → A204 → A205 → A402 → A404
（DB DDL → 認証基盤 → ICurrentUser → middleware → auth endpoint → WinForms LoginForm → 再ログイン）

## Phase B 申し送り (本イシュー群では作らない)

- refresh token + rotation 実装
- 複数ロール兼務（DB は既に多対多なので値追加のみ）
- テナント分離（org_id を全 SQL の WHERE に強制）
- audit_log の `actor TEXT` 列を Phase B 開始時の PR で正式に `display_name` 列へリネーム

詳細は採択案「案 P」末尾、および各イシューファイルの「実装ノート」「テスト観点」を参照。
