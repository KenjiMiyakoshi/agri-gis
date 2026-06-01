# テスト方針

採択設計「案 B'」に基づく本プロジェクトのテスト方針を明文化する。
このドキュメントは「何をテストし、何をテストしないか」の宣言であり、
新規参加者の判断軸とコードレビューの揺れ止めとして機能する。

---

## 1. テストの目的

テストは以下の 2 つの役割に集中する。これ以外の目的にテストを使わない。

1. **仕様の固定**：システムが満たすべき外部仕様（バイテンポラル整合、監査ログの完備、HTTP ステータスマップ、ProblemDetails 形）を**実行可能な形で残す**。仕様書が陳腐化しても、テストが落ちれば仕様が破壊されたと検知できる。
2. **リファクタの安全網**：将来、内部実装（PL/pgSQL の関数化方針、Minimal API の構造、DI 構成など）を組み替えた際に、観測される振る舞いが変わらないことを担保する。

「網羅性のためのテスト」「カバレッジ数値達成のためのテスト」は書かない。**書かれたテストには必ず仕様または不変条件の主張が紐付いていること**。

---

## 2. テスト対象の優先順位

採択案 B' の中核要件である**バイテンポラル + 監査ログ**を最優先に守る。優先順は：

| 優先順位 | 対象 | 例 |
|---|---|---|
| 最優先 | **不変条件**：`feature_current` / `feature_history` / `audit_log` の行数・整合性 | INSERT 後 current=+1 / history=0 / audit=+1 |
| 高 | HTTP 経由のハッピーパス + 主要エラーマッピング (400/404/409/422/428) | PATCH `If-Match` 不整合で 409、X-Actor 無しで 400 |
| 高 | バイテンポラル経路 (asOf, layer_schema_version の append-only) | asOf=過去日付で history 行が UNION ALL に含まれる |
| 中 | 純粋関数の単体テスト（`Core/AttributeValidator` 等） | required 欠落で 1 件のエラーが返る |
| 中 | Convention 検査（`Core` から `System.Windows.Forms` 参照禁止） | リフレクションで型参照を走査 |
| 低 | メッセージ envelope のシリアライズ往復（WebGIS / WinForms 双方の bridge） | requestId 重複検知 |
| **対象外** | WebView2 のレンダリング | — |
| **対象外** | OpenLayers の見た目（色 / フォント / 回転モーション） | — |
| **対象外** | WinForms UI 自動操作（ボタン押下シナリオ） | — |

「対象外」は CI で自動化しない、という意味であり、手動の動作確認は別途行う。

---

## 3. カバレッジ目標を立てない理由

本サイクルでは**カバレッジ数値目標を掲げない**。これは故意の判断であり、以下の理由による。

- **数値目標は無意味なテストを生む**：80% 等の目標は、Getter/Setter を呼ぶだけの「カバレッジ稼ぎ」を誘発する。それらは仕様も不変条件も主張しないため、リファクタの足かせにしかならない。
- **退行検知力はカバレッジでは測れない**：本当に欲しいのは「実装が壊れたら落ちる」性質であり、これを測るなら mutation testing が筋。カバレッジは「実行されたかどうか」しか測らない。
- **不変条件テストの方が ROI が高い**：1 本の不変条件テスト（current/history/audit の行数）は、5 〜 10 本のユニットテストと同等以上の退行検知力を持つ。

将来、mutation testing（Stryker.NET 等）を導入するかは別途検討。本サイクルは導入しない。

---

## 4. テスト分類

| 分類 | プロジェクト | 主な道具 | 走らせ方 |
|---|---|---|---|
| API 結合 | `api.tests/AgriGis.Api.Tests` | xUnit + `Microsoft.AspNetCore.Mvc.Testing` + `Testcontainers.PostgreSql` + `Npgsql` | `dotnet test`（Docker Desktop 必須） |
| Core 単体 | `windos-app.tests/AgriGis.Desktop.Tests`（#37 で導入） | xUnit | `dotnet test`（依存なし） |
| Convention | 同上 | リフレクションで `Core/*.cs` の `using` を検査 | 同上 |
| WebGIS 単体 | `webgis/`（Vitest、#32 で導入） | Vitest + jsdom | `npm run test` |

**E2E (WebView2 を起動して WinForms↔WebGIS を一気通貫で叩く) は本サイクルでは作らない**。投資対効果が悪い（Windows 必須、Docker と WebView2 ランタイムの両方が整った CI が必要）。手動の動作確認手順は README 末尾に残す。

---

## 5. テストデータ規約

### 5.1 DB リセット

各テストクラスのコンストラクタまたは `IAsyncLifetime.InitializeAsync` で `DbReset.RunAsync(connStr)` を呼ぶ。これにより：

- `feature_current` / `feature_history` / `audit_log` / `layer_schema_version` が `TRUNCATE ... RESTART IDENTITY CASCADE`
- `layers` を再シード（2 レイヤ：サンプル圃場 (polygon) と サンプル観測点 (point)、それぞれ最小 schema_json 付き）
- `layer_schema_version` に初期 1 行ずつ

`feature_id` / `history_id` / `audit_id` の連番が毎テストで 1 から始まることが保証される。

### 5.2 表示用シード

`db/init/002_seed.sql` が新規環境用のシード。本ファイルは「マイグレーション適用後のスキーマ前提」で書かれている（#11 で対応）。
テスト内では `DbReset.RunAsync` が独自の最小シードを投入するため、`002_seed.sql` 自体には依存しない。

### 5.3 過去日付の生成

`asOf` 系のテストで過去日付を扱う際は、テスト内で `CURRENT_DATE - N` を生成する。
ハードコードした「2025-12-01」のような日付は使わない（将来のテスト実行日でテストが落ちる）。

例：
```csharp
var pastDate = DateTime.UtcNow.Date.AddDays(-3).ToString("yyyy-MM-dd");
```

### 5.4 actor

書き込み系テストでは原則 `WithActor("alice")` のような明示値を使う。実際のユーザー名や `Environment.UserName` を使わない（CI で値が変わる）。

---

## 関連ドキュメント

- 既存イシュー: [docs/issues/0303-test-invariants.md](issues/0303-test-invariants.md), [docs/issues/0304-test-extra-cases.md](issues/0304-test-extra-cases.md)
- 採択設計：本リポジトリの設計・レビュー記録（チャットログ）と `docs/issues/README.md` を参照
