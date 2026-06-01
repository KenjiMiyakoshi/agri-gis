# A601: docs/auth.md (JWT claims, role matrix, 鍵管理)

| 項目 | 値 |
|---|---|
| Phase | Docs |
| Estimate | 0.5d |
| Depends on | A201, A205, A206 |
| Blocks | なし |

## 概要
`docs/auth.md` を新規作成し、認証基盤の仕様（JWT claims、role matrix、鍵管理、運用手順）をドキュメント化する。

## 背景・目的
採択案「案 P」を実装した結果として、外部開発者・運用者が理解すべき認証仕様を明文化する。Phase B の refresh token 実装、テナント分離の前提資料にもなる。

## スコープ
### 含む
- `docs/auth.md`:
  - 認証フロー図（テキスト or ASCII）
  - JWT claims 仕様一覧
  - role / policy matrix
  - 環境変数 (`AGRI_GIS_JWT_SECRET`, `AGRI_GIS_INITIAL_ADMIN_PW`, `AGRI_GIS_INITIAL_ADMIN_LOGIN_ID`, `AGRI_GIS_INITIAL_ADMIN_ORG_CODE`)
  - 鍵管理: secret は 32 byte 以上、ローテーション手順 (Phase A は手動再起動、Phase B で JWKS)
  - パスワードポリシー: 最低 8 文字、BCrypt work factor 11
  - 401 / 403 の ProblemDetails スキーマ
  - 初期 admin の upsert 仕様
  - Phase B 申し送り（refresh token、複数 role、テナント分離、audit actor 列リネーム）

### 含まない
- README 更新 (A602)
- API リファレンス (OpenAPI から自動生成、別ライン)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `docs/auth.md` が存在
- [ ] JWT claims の表が含まれる（sub/name/role/org_id/iss/aud/exp/iat/jti/display_name）
- [ ] role × エンドポイント matrix が含まれる
- [ ] 環境変数一覧が含まれる
- [ ] Phase B 申し送りが含まれる

## 影響ファイル
- `D:\proj\agri-gis\docs\auth.md` (新規)

## 実装ノート
構成:
```
# 認証 / 認可 仕様 (Phase A)

## 概要
HS256 JWT による access only 認証。refresh は Phase B。

## JWT claims
| claim | 型 | 説明 |
|---|---|---|
| sub | UUID string | users.user_id |
| name | string | login_id |
| display_name | string | users.display_name |
| role | string (multi) | admin / general / guest |
| org_id | int string | users.org_id |
| iss | string | 設定値 |
| aud | string | 設定値 |
| exp | unix time | 発行 + 8h |
| iat | unix time | 発行時刻 |
| jti | UUID | 一意 |

## ロール
- admin: 全権、Admin CRUD 可
- general: 通常ユーザ、Feature の CRUD 可
- guest: 読み取り専用 (GET のみ)、JWT は必須

## 環境変数
- `AGRI_GIS_JWT_SECRET` (required, >= 32 byte UTF-8)
- `AGRI_GIS_INITIAL_ADMIN_PW` (required)
- `AGRI_GIS_INITIAL_ADMIN_LOGIN_ID` (default: admin)
- `AGRI_GIS_INITIAL_ADMIN_ORG_CODE` (default: SYSTEM)

## エラー応答
401 / 403 はすべて `application/problem+json` (A203)。

## 運用
- secret ローテーション: 環境変数差し替え + 全再起動 (Phase A、すべてのトークンが失効)
- 初期 admin パスワード変更: `AGRI_GIS_INITIAL_ADMIN_PW` 更新 + 再起動 (upsert)
- 緊急復旧: 同上

## Phase B 申し送り
- refresh token + rotation
- 複数ロール兼務（DB は既に多対多）
- テナント分離 (org_id を全 SQL WHERE 強制)
- audit_log.actor TEXT 列を display_name にリネーム
```

注意点:
- 日本語で記述（リポジトリの既存 docs と一貫性）
- Markdown table を活用

## テスト観点
- ドキュメントのため自動テスト不要
- レビュー時に採択案「案 P」との一貫性確認
