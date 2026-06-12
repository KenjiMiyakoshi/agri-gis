# db/migration/

連番マイグレーション SQL を置くディレクトリ。既存環境への差分適用に使う。

新規環境の初期化用 SQL は `db/init/` 配下（`docker-compose.yml` の `docker-entrypoint-initdb.d` にマウントされ、PostgreSQL コンテナの**初回起動時のみ**自動実行される）。
本ディレクトリは初回起動以降の**スキーマ進化**を担う。

## 命名規則

```
db/migration/NNN_<slug>.sql
```

- `NNN`: 3桁の連番（`001` から開始）
- `<slug>`: 内容を短く表す英数 + ハイフン区切り (例: `002_layers_schema_json.sql`)

## 運用ルール

1. **配置済みのファイルは編集禁止**。中身を変えたい場合は新しい番号を切る。
2. **冪等性必須**：何度流しても結果が同じになるように書く。
   - 新規テーブル: `CREATE TABLE IF NOT EXISTS ...`
   - 列追加: `ALTER TABLE ... ADD COLUMN IF NOT EXISTS ...`
   - 型変更/列変更: 既存値を保持したまま安全に行えるか確認
   - 関数: `CREATE OR REPLACE FUNCTION ...`（戻り型変更時は `DROP FUNCTION ...` を別マイグレーションで先行）
   - インデックス: `CREATE INDEX IF NOT EXISTS ...`
3. **適用順序**：連番順。先行する番号への依存は許容。逆方向（後ろの番号に依存）は不可。
4. **トランザクション境界**：原則1ファイル=1論理変更。複数文を含む場合は冒頭に `BEGIN;` / 末尾に `COMMIT;` を書く（PostgreSQL は DDL もトランザクション可能）。

## 適用方法（暫定）

マイグレーションツール（Flyway / dbmate 等）は本サイクルでは導入しない。手動で適用する：

```powershell
# 1ファイル適用
docker exec -i agri_postgis psql -U agri_user -d agri_gis < db/migration/NNN_xxx.sql

# 連続適用（PowerShell）
# Sort-Object の既定は culture-aware で、Windows では `_` を `.` より小さく扱うため、
# 例えば `0F03_org_layer_permission_backfill.sql` が `0F03_org_layer_permission.sql` の前に
# 並んでしまう。ordinal (CurrentCultureIgnoreCase ではなく) 比較を強制する。
Get-ChildItem db/migration/*.sql |
  Sort-Object @{ Expression = { $_.Name }; Ascending = $true } -CaseSensitive |
  ForEach-Object {
    Write-Host "Applying $($_.Name)..."
    Get-Content $_.FullName -Raw | docker exec -i agri_postgis psql -U agri_user -d agri_gis
  }
```

## 既存 `db/init/` との関係

| ディレクトリ | 役割 | 適用契機 |
|---|---|---|
| `db/init/` | 新規環境の初期スキーマとシード | コンテナ初回起動時に自動 |
| `db/migration/` | 既存環境への差分適用 | 手動（上記コマンド） |

新規環境を立ち上げる場合は `db/init/` のみで動く状態を保つ（マイグレーションを後追いしないと壊れる、という状態は避ける）。`db/init/` は最新スキーマのスナップショットとして時々更新する想定（本サイクル外）。

## 関連イシュー
- `0101` (本ディレクトリ整備)
- `0102` `layers` 拡張
- `0103` `feature_current` 拡張
- `0104` `feature_history` 新設
- `0105` `audit_log` 新設
- `0106` `layer_schema_version` 新設
- `0107`-`0110` PL/pgSQL 関数
- `0111` シード更新
