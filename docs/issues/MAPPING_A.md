# Phase A Issue 番号対応表

`docs/issues/A1xx-A6xx-*.md` の元番号と GitHub Issues 番号の対応表。

Phase A (認証基盤) は **マイルストーン `Phase A: 認証基盤`** に集約。
本文中の `Depends on: A101` のような表記は元番号のまま残してある。

リポジトリ: <https://github.com/KenjiMiyakoshi/agri-gis>

## A1xx DB (phase/db)

| 元番号 | GitHub | タイトル |
|---|---|---|
| A101 | [#77](https://github.com/KenjiMiyakoshi/agri-gis/issues/77) | organizations / users / user_roles DDL |
| A102 | [#78](https://github.com/KenjiMiyakoshi/agri-gis/issues/78) | audit_log に actor_user_id 追加 + actor を display_name 化 |
| A103 | [#79](https://github.com/KenjiMiyakoshi/agri-gis/issues/79) | feature_current(entity_id) UNIQUE INDEX 追加 (H1 修復) |
| A104 | [#80](https://github.com/KenjiMiyakoshi/agri-gis/issues/80) | C1 修復 - fn_feature_update の valid_from/valid_to を CURRENT_DATE で接合 |
| A105 | [#81](https://github.com/KenjiMiyakoshi/agri-gis/issues/81) | C2 修復 - 4関数の to_jsonb から geom を抜き geom_geojson を追加 |
| A106 | [#82](https://github.com/KenjiMiyakoshi/agri-gis/issues/82) | PL/pgSQL 関数引数の非破壊拡張 (p_user_id, p_org_id) |

## A2xx API (phase/api)

| 元番号 | GitHub | タイトル |
|---|---|---|
| A201 | [#83](https://github.com/KenjiMiyakoshi/agri-gis/issues/83) | BCrypt.Net-Next + JwtBearer + appsettings/環境変数 |
| A202 | [#84](https://github.com/KenjiMiyakoshi/agri-gis/issues/84) | ICurrentUser interface + HttpContextCurrentUser |
| A203 | [#85](https://github.com/KenjiMiyakoshi/agri-gis/issues/85) | IAuthorizationMiddlewareResultHandler で 401/403 を ProblemDetails 統合 |
| A204 | [#86](https://github.com/KenjiMiyakoshi/agri-gis/issues/86) | RequestContext から RequireActor 廃止 + middleware 順序見直し |
| A205 | [#87](https://github.com/KenjiMiyakoshi/agri-gis/issues/87) | POST /api/auth/login + GET /api/auth/me + POST /api/auth/change-password |
| A206 | [#88](https://github.com/KenjiMiyakoshi/agri-gis/issues/88) | 既存エンドポイントへの [Authorize] / [AllowAnonymous] 配置 |
| A207 | [#89](https://github.com/KenjiMiyakoshi/agri-gis/issues/89) | 初期 admin の IHostedService による upsert |

## A3xx AdminCrud (phase/admincrud)

| 元番号 | GitHub | タイトル |
|---|---|---|
| A301 | [#90](https://github.com/KenjiMiyakoshi/agri-gis/issues/90) | /api/admin/organizations CRUD |
| A302 | [#91](https://github.com/KenjiMiyakoshi/agri-gis/issues/91) | /api/admin/users CRUD + パスワード変更分離 |

## A4xx WinForms (phase/winforms)

| 元番号 | GitHub | タイトル |
|---|---|---|
| A401 | [#92](https://github.com/KenjiMiyakoshi/agri-gis/issues/92) | ActorContext 削除 + ISessionStore / InMemorySessionStore 新設 |
| A402 | [#93](https://github.com/KenjiMiyakoshi/agri-gis/issues/93) | LoginForm + Designer + 起動フロー |
| A403 | [#94](https://github.com/KenjiMiyakoshi/agri-gis/issues/94) | BearerHandler + ApiClient.LoginAsync + X-Actor 削除 |
| A404 | [#95](https://github.com/KenjiMiyakoshi/agri-gis/issues/95) | 401 再ログインフロー + Guest UI 制限 |

## A5xx Test (phase/test)

| 元番号 | GitHub | タイトル |
|---|---|---|
| A501 | [#96](https://github.com/KenjiMiyakoshi/agri-gis/issues/96) | SeedUsers fixture + DbReset で users/orgs seed |
| A502 | [#97](https://github.com/KenjiMiyakoshi/agri-gis/issues/97) | TokenForge + ApiClientFactory.WithActorAs 明示形 |
| A503 | [#98](https://github.com/KenjiMiyakoshi/agri-gis/issues/98) | 既存 AsOfTests の手動 UPDATE 削除 + MissingActorTests → AuthRequiredTests |
| A504 | [#99](https://github.com/KenjiMiyakoshi/agri-gis/issues/99) | AuthLoginTests + JwtValidationTests + AnonymousReadTests |
| A505 | [#100](https://github.com/KenjiMiyakoshi/agri-gis/issues/100) | AuthorizationTests (3 role × エンドポイント matrix) |
| A506 | [#101](https://github.com/KenjiMiyakoshi/agri-gis/issues/101) | AdminUsersCrudTests + AdminOrgsCrudTests |
| A507 | [#102](https://github.com/KenjiMiyakoshi/agri-gis/issues/102) | AuditUserIdTests + AuditLogGeomStripTests (C2 回帰) |
| A508 | [#103](https://github.com/KenjiMiyakoshi/agri-gis/issues/103) | C1RegressionTests + BcryptHashTests + InitialAdminSeedTests |

## A6xx Docs (phase/docs)

| 元番号 | GitHub | タイトル |
|---|---|---|
| A601 | [#104](https://github.com/KenjiMiyakoshi/agri-gis/issues/104) | docs/auth.md (JWT claims, role matrix, 鍵管理) |
| A602 | [#105](https://github.com/KenjiMiyakoshi/agri-gis/issues/105) | README 更新 (起動手順に LoginForm + 環境変数) |

## サマリ

| フェーズ | 件数 | GitHub 番号レンジ | 工数 |
|---|---|---|---|
| A1xx DB | 6 | #77-#82 | 4.5d |
| A2xx API | 7 | #83-#89 | 5.0d |
| A3xx AdminCrud | 2 | #90-#91 | 2.0d |
| A4xx WinForms | 4 | #92-#95 | 2.5d |
| A5xx Test | 8 | #96-#103 | 6.0d |
| A6xx Docs | 2 | #104-#105 | 1.0d |
| **合計** | **29** | **#77-#105** | **21.0d** |

マイルストーン: <https://github.com/KenjiMiyakoshi/agri-gis/milestone/5>
