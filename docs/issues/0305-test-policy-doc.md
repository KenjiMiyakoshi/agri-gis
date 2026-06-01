# 0305: `docs/testing-policy.md` 執筆

| 項目 | 値 |
|---|---|
| Phase | Test |
| Estimate | 0.5d |
| Depends on | 0301 |
| Blocks | なし |

## 概要
本プロジェクトのテスト方針を `docs/testing-policy.md` に明文化する。「何をテストし、何をテストしないか」を宣言する。

## 背景・目的
案 B' は意図的に「カバレッジ目標を持たない」「不変条件と外部仕様に焦点を絞る」立場。これを文章で残さないとレビューで揺れる。

## スコープ
### 含む
- `docs/testing-policy.md` を新規作成
- セクション:
  1. テストの目的（仕様の固定 / リファクタの安全網）
  2. テスト対象の優先順位
     - 最優先: 不変条件 (current / history / audit)
     - 次点: HTTP 経由のハッピーパス + 主要エラーマッピング
     - 次点: 純粋関数 (Core/AttributeValidator 等) の単体
     - 対象外: WebView2 描画、OL の見た目、WinForms UI 自動操作
  3. カバレッジ目標は **掲げない** 理由
     - 数値目標は無意味なテストを生む / 退行検知力を測るなら mutation testing の方が良い
     - 本サイクルは導入なし、将来検討
  4. テスト分類
     - API 結合 (`AgriGis.Api.Tests`)
     - Core 単体 (WinForms 0505)
     - Convention (`Core` から `System.Windows.Forms` 参照禁止)
     - WebGIS 単体 (Vitest, envelope / 重複検知)
  5. テストデータ規約
     - 各テストで `DbReset.RunAsync` を呼ぶ
     - 表示用シードは固定 (0111)
     - 過去日付は `CURRENT_DATE - N` で生成

### 含まない
- 個別ケースの列挙

## 受け入れ条件 (Acceptance Criteria)
- [ ] `docs/testing-policy.md` が存在
- [ ] 上記の 5 セクションがある
- [ ] 「カバレッジ目標を立てない理由」が明示されている

## 影響ファイル
- `D:\proj\agri-gis\docs\testing-policy.md` (新規)

## 実装ノート
- 1500 〜 2500 字、Markdown 見出し付きで構成
- 0303, 0304 のシナリオへの参照リンクを入れる（相対パス）

## テスト観点
- ドキュメントなのでテストなし
