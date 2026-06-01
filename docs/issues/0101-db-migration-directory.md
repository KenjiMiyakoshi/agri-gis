# 0101: `db/migration/` ディレクトリ整備とマイグレーション運用方針

| 項目 | 値 |
|---|---|
| Phase | DB |
| Estimate | 0.5d |
| Depends on | なし |
| Blocks | 0102, 0103, 0104, 0105, 0106, 0107, 0108, 0109, 0110, 0301 |

## 概要
連番マイグレーション用の `db/migration/` ディレクトリと運用ルールを定義する。既存の `db/init/` は新規 DB の初期化用に残す。

## 背景・目的
案 B' は既存 `db/init/001_init.sql` に追加カラムや関数を被せる必要がある。`init/` だけだと初回起動時しか流れず差分管理ができないので、連番でかつ何度流しても結果が同じになる SQL ファイル群を置く `db/migration/` を用意する。

## スコープ
### 含む
- `db/migration/` ディレクトリ作成
- 連番ルール (`NNN_<slug>.sql`、3桁、001 から) の文書化
- 「マイグレーション SQL は冪等 (`IF NOT EXISTS` 等) であること」のルール明記
- `db/migration/README.md` でルール記載
- `docker-compose.yml` の `db/init` マウントは現状維持（初期化のみ。マイグレーションは別経路で適用する旨を明記）

### 含まない
- マイグレーションツール (Flyway 等) の導入
- 実際のマイグレーション SQL の中身 (個別イシューで対応)

## 受け入れ条件 (Acceptance Criteria)
- [ ] `db/migration/` ディレクトリが存在する
- [ ] `db/migration/README.md` に命名ルール・冪等性ルール・適用順序が書かれている
- [ ] 既存 `db/init/001_init.sql`, `002_seed.sql` は変更なし（このイシューでは触らない）

## 影響ファイル
- `D:\proj\agri-gis\db\migration\` (新規)
- `D:\proj\agri-gis\db\migration\README.md` (新規)

## 実装ノート
- `db/migration/README.md` に書く内容
  - 連番: `001_*.sql` から
  - 一度配置したファイルは編集禁止（中身を変えたい時は新しい番号を切る）
  - 冪等性: `CREATE TABLE IF NOT EXISTS`, `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`, `CREATE OR REPLACE FUNCTION` などを使う
  - 関数 (`fn_*`) は `CREATE OR REPLACE FUNCTION` で都度上書き
  - 適用方法（暫定）: `psql -U agri_user -d agri_gis -f db/migration/NNN_xxx.sql`

## テスト観点
- 全ファイルを連続適用しても 2 回目以降エラーにならない（後続イシューの統合テストで確認）
