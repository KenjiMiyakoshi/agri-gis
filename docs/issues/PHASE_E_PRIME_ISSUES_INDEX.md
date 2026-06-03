# Phase E' Issues Index

Phase E' で起票する全 14 Issue + 補助 1 件の一覧。

ラベル: `phase:E-prime`, `wave:WE'N`, `area:db|api|webgis|winforms|tests|docs`

## WE'0 — Plan + Design

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| E'100 | Phase E' Plan + Design 3 本 + GetFeatureInfo ノート | docs | 0.5d |

## WE'1 — DB クロージング + DbReset 並列耐性

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| E'101 | `api.tests/Fixtures/DbReset.cs` の `layer_style_version` / `layer_history` TRUNCATE 追加 + xunit Collection 設定見直し | tests | 0.3d |
| E'102 | `0E08_drop_layers_deleted_at.sql` (up + down) + `LayerAdminDto.DeletedAt` 削除 | db | 0.3d |
| E'103 | `fn_layer_delete v3` / `fn_layer_update` / `fn_layer_style_upsert` の `deleted_at` 経路削除 | db | 0.4d |
| E'104 | API endpoint 4 ファイル / 18 SQL 箇所の WHERE 条件置換 + テスト書換 | api | 0.5d |

## WE'2 — WinForms asOf 配線

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| E'201 | `IApiClient` に `DateOnly? asOf` 引数追加 (5 メソッド) | winforms | 0.2d |
| E'202 | `ApiClient` 実装 + `AppendAsOf` 共通 helper | winforms | 0.3d |
| E'203 | `AsOfState.cs` 新規 + MainForm 統合 | winforms | 0.4d |
| E'204 | `AsOfStateTests` 5 件 (MainFormAsOfPickerTests 軽量版) | tests | 0.4d |
| E'205 | `FakeApiClient` + 既存 38 件 windows-app.tests の null 渡し fix | tests | 0.2d |

## WE'3 — API テスト追加

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| E'301 | `LayersEndpointsStyleVersionTests` (3 ケース) | tests | 0.4d |
| E'302 | `TilesEndpointsCacheControlTests` (3 ケース) + `FakeGeoServerProxy.cs` | tests | 0.6d |

## WE'4 — WinForms SSE + batch UI + Docs

| ID | Issue タイトル | area | 工数 |
|----|---------------|------|------|
| E'401 | `windos-app/Services/LayerEventListener.cs` 新規 (SSE + IObservable + 自動 reconnect) | winforms | 0.5d |
| E'402 | MainForm 統合: SSE subscribe / unsubscribe / 受信時 bridge envelope 発火 | winforms | 0.3d |
| E'403 | `BatchAttributeEditDialog` 新規 + DataGridView 複数選択 → batch 編集 | winforms | 0.5d |
| E'404 | `LayerEventListenerTests` + `BatchAttributeEditDialogTests` 7 件 | tests | 0.2d |
| E'500 | `PHASE_E_PRIME_COMPLETE.md` + メモリ更新 + README 補正 (WE'4 PR に同梱) | docs | 0.1d |

## 起票時のテンプレート

```markdown
## 課題
(Plan の §X.1 をコピー)

## 採用方針
(Plan の §X.2 採用案をコピー)

## 影響範囲
(Plan の §X.3 をコピー)

## 受入条件
- [ ] (Wave Plan の検証項目)
- [ ] テストが green (`-c Release`)

## 関連
- 親 Wave: WE'N (#N)
- Design: docs/XXX.md
```

## マイルストーン

`Phase E': Phase E クロージング + asOf 全面伝搬 + SSE WinForms 統合`

## 並列実行の指針

- 各 Wave 内: 同 Wave 内の独立 Issue は同 PR にまとめる
- Wave 間: WE'2 / WE'3 は WE'1 完了後に並列着手可
- WE'4 は WE'2 完了後 (MainForm 改修ベース共有)
