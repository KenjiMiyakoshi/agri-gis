# 0602: `README.md` 更新 (新アーキ、起動手順、Desktop)

| 項目 | 値 |
|---|---|
| Phase | Docs |
| Estimate | 0.5d |
| Depends on | 0211, 0504 |
| Blocks | なし |

## 概要
プロジェクトルートの `README.md` を案 B' の全体像に合わせて更新する。

## 背景・目的
案 B' の追加要素（バイテンポラル / 監査 / WinForms クライアント / 新エンドポイント / マイグレーション運用）を反映し、新規開発者が迷わず起動・開発できるようにする。

## スコープ
### 含む
- ディレクトリ一覧の更新（`db/migration/`, `api.tests/`, `windos-app/`, `AgriGis.Desktop.Tests/`, `docs/`）
- 起動手順
  1. `docker compose up -d`
  2. `db/migration/*.sql` を psql で適用（順番）
  3. `dotnet run --project api`
  4. `cd webgis && npm install && npm run dev`
  5. `dotnet run --project windos-app`
- エンドポイント仕様の更新（全 11 本: layers x3, features x6, admin x1 + health）
  - 各エンドポイントの簡易表（メソッド、パス、必須ヘッダ、ボディ概形、HTTP コード）
- 設計の柱
  - バイテンポラル: valid_from/valid_to は DATE
  - 監査: audit_log に全書き込みが残る
  - 楽観ロック: `If-Match` ヘッダ
  - スキーマバージョニング: layer_schema_version の append-only
- リンク
  - `docs/issues/README.md` (イシュー一覧)
  - `docs/testing-policy.md`
  - `docs/message-protocol.md`
- 既知の制約
  - 認証なし（actor はクライアントが申告）
  - 図形編集 UI なし（API は受ける）

### 含まない
- 各イシューの中身の繰り返し
- アーキ図（あれば嬉しいが本サイクル外）

## 受け入れ条件 (Acceptance Criteria)
- [ ] ディレクトリツリーに `windos-app/`, `api.tests/`, `docs/issues/` が含まれる
- [ ] 起動手順 5 ステップが書かれている
- [ ] 全 API エンドポイントが表で列挙されている
- [ ] バイテンポラル / 監査 / 楽観ロック / スキーマバージョニングの 1 行説明がある

## 影響ファイル
- `D:\proj\agri-gis\README.md` (変更)

## 実装ノート
- 既存 README は表とコードブロックを多用するスタイル。これを踏襲
- `psql` 例:
  ```powershell
  $env:PGPASSWORD = 'agri_pass'
  Get-ChildItem db/migration/*.sql | ForEach-Object {
    psql -h localhost -U agri_user -d agri_gis -f $_.FullName
  }
  ```

## テスト観点
- ドキュメント。起動手順の通りに動くかは手動 smoke で確認
