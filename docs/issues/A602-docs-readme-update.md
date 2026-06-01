# A602: README 更新 (起動手順に LoginForm + 環境変数)

| 項目 | 値 |
|---|---|
| Phase | Docs |
| Estimate | 0.5d |
| Depends on | A207, A402, A601 |
| Blocks | なし |

## 概要
リポジトリ直下の `README.md` (および `windos-app/README.md` 等あれば) を更新し、Phase A の起動手順（環境変数、LoginForm フロー）を反映する。

## 背景・目的
新規参加者が clone してから動かせるまでの手順を最新化。`AGRI_GIS_JWT_SECRET` と `AGRI_GIS_INITIAL_ADMIN_PW` を知らないと起動失敗するため、これを明示する。

## スコープ
### 含む
- README に「環境変数の設定」セクション追加
- API 起動手順: `dotnet run --project api` 前の export 例
- WinForms 起動手順: LoginForm が出ること、初期 admin での login 手順
- 開発者向け seed: `dotnet test` 実行前の `AGRI_GIS_JWT_SECRET` 設定
- docs/auth.md へのリンク

### 含まない
- 詳細な API リファレンス（auth.md 側）
- CI 設定（別途）

## 受け入れ条件 (Acceptance Criteria)
- [ ] README に環境変数一覧と例が含まれる
- [ ] LoginForm の存在と初期 admin 手順が説明されている
- [ ] docs/auth.md へのリンクがある
- [ ] `dotnet test` を走らせるための環境変数も記載

## 影響ファイル
- `D:\proj\agri-gis\README.md`
- (存在すれば) `D:\proj\agri-gis\windos-app\README.md`
- (存在すれば) `D:\proj\agri-gis\api\README.md`

## 実装ノート
README の追加例（既存内容を尊重しつつ section 追加）:

```markdown
## 起動手順 (Phase A 以降)

### 必須環境変数
| 変数 | 必須 | 説明 |
|---|---|---|
| `AGRI_GIS_JWT_SECRET` | 必須 | HS256 用シークレット、32 byte 以上の UTF-8 文字列 |
| `AGRI_GIS_INITIAL_ADMIN_PW` | 必須 | 初期 admin パスワード、起動時 upsert |
| `AGRI_GIS_INITIAL_ADMIN_LOGIN_ID` | 任意 | デフォルト `admin` |
| `AGRI_GIS_INITIAL_ADMIN_ORG_CODE` | 任意 | デフォルト `SYSTEM` |

### API
```powershell
$env:AGRI_GIS_JWT_SECRET = "change-me-this-must-be-at-least-32bytes!!"
$env:AGRI_GIS_INITIAL_ADMIN_PW = "ChangeMe123"
dotnet run --project api
```

### WinForms
1. 上記環境変数で API を起動
2. `dotnet run --project windos-app`
3. LoginForm が表示される
4. login_id `admin`, password (`AGRI_GIS_INITIAL_ADMIN_PW` の値) でログイン

### テスト実行
```powershell
$env:AGRI_GIS_JWT_SECRET = "test-secret-must-be-at-least-32bytes!!!"
dotnet test
```

詳細は [docs/auth.md](docs/auth.md) を参照。
```

注意点:
- 既存 README の構成を尊重しつつ追記
- PowerShell と bash 両方のサンプル提供推奨

## テスト観点
- 手動: README の手順通りに動くか smoke
