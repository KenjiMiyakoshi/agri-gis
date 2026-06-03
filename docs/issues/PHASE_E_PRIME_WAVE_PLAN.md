# Phase E' Wave Plan

## クリティカルパス

```
WE'0 (Plan + Design 3 本 + GetFeatureInfo ノート, 0.5d)
   │
   ▼
WE'1 (DB: deleted_at DROP + DbReset 並列耐性, 1.5d)
   │        ┌──────────────┐
   ▼        ▼              ▼
WE'2 (WinForms asOf + AsOfState, 1.5d)  ⫽  WE'3 (API テスト追加, 1.0d)
   │
   ▼
WE'4 (WinForms SSE + batch UI + Docs, 1.5d)
```

合計 6.0d。クリティカルパス WE'0 → WE'1 → WE'2 → WE'4 = 5 営業日。WE'3 は WE'2 と並列消化 (両者 WE'1 完了が前提)。

## WE'0 — Plan + Design + GetFeatureInfo ノート (Gate)

ブランチ: `feature/phase-e-prime-we0-design`

| Issue | 内容 | 工数 |
|-------|------|------|
| **E'100** | Plan 3 本 + Design 3 本 + GetFeatureInfo ノート 1 本 | 0.5d |

成果物:
- `docs/PHASE_E_PRIME_INDEX.md` (本 PR で作成)
- `docs/issues/PHASE_E_PRIME_{PLAN,WAVE_PLAN,ISSUES_INDEX}.md`
- `docs/layers-deleted-at-drop.md` (Design)
- `docs/test-isolation.md` (Design + 並列耐性 PoC 結果メモ)
- `docs/winforms-event-listener.md` (Design)
- `docs/wms-getfeatureinfo-eprime2-note.md` (E'' 送りノート)

検証:
- 全 7 ドキュメント markdown lint pass
- DbReset 並列耐性問題の原因特定が記録される (test-isolation.md)

## WE'1 — DB クロージング + DbReset 並列耐性

ブランチ: `feature/phase-e-prime-we1-db-and-test-isolation`

| Issue | 内容 | 工数 |
|-------|------|------|
| **E'101** | `api.tests/Fixtures/DbReset.cs` の `layer_style_version` / `layer_history` TRUNCATE 追加 + xunit Collection 設定見直し | 0.3d |
| **E'102** | `0E08_drop_layers_deleted_at.sql` (up + down) + `LayerAdminDto.DeletedAt` 削除 | 0.3d |
| **E'103** | `fn_layer_delete v3` / `fn_layer_update` / `fn_layer_style_upsert` から `deleted_at` 操作削除 (CREATE OR REPLACE 3 本) | 0.4d |
| **E'104** | API endpoint 4 ファイル / 18 SQL 箇所の WHERE 条件 `AND l.deleted_at IS NULL` → `AND l.valid_to = '9999-12-31'::date` 置換 + テスト書換 (`FeatureEndpointsDeletedAtRegressionTests` 等) | 0.5d |

検証:
- migration 0E08 を local DB に適用、`\d layers` で deleted_at 列消滅確認
- `dotnet test api.tests -c Release` 全 green (E'101 解決後、83+ 件 pass)
- 単独実行と full run で同じ件数 pass (並列耐性回帰なし)

並列度: E'101 → (E'102, E'103, E'104 順次、E'104 が一番大きい)。

## WE'2 — WinForms asOf 配線 + AsOfState

ブランチ: `feature/phase-e-prime-we2-winforms-asof`

| Issue | 内容 | 工数 |
|-------|------|------|
| **E'201** | `IApiClient` に `DateOnly? asOf` 引数追加 (5 メソッド: GetLayersAsync, GetLayerSchemaAsync, GetLayerStyleAsync, ListLayersAdminAsync, BatchUpdateFeaturesAsync) | 0.2d |
| **E'202** | `ApiClient` 実装 + `AppendAsOf` 共通 helper | 0.3d |
| **E'203** | `windos-app/ViewModels/AsOfState.cs` 新規 (Current / IsReadOnly / Changed イベント) + MainForm 統合 | 0.4d |
| **E'204** | `MainFormAsOfPickerTests` 系 unit test 5 件: AsOfStateTests | 0.4d |
| **E'205** | 既存 `FakeApiClient` (windos-app.tests) を新シグネチャに修正 + 既存 38 件 windows-app.tests の `null` 渡し fix | 0.2d |

検証:
- `dotnet build windos-app -c Release` 0 warnings/errors
- `dotnet test windos-app.tests -c Release` 全 green (118 + 5 = 123 件)
- 手動: 過去時点モード ON → MainForm レイヤ一覧の `GET /api/layers?asOf=YYYY-MM-DD` URL 確認 (DevTools)

並列度: E'201 → E'202 → E'203 + E'205 並列、E'204 は E'203 完了後。

## WE'3 — API テスト追加 (WE'1 と並列可能)

ブランチ: `feature/phase-e-prime-we3-api-tests`

| Issue | 内容 | 工数 |
|-------|------|------|
| **E'301** | `LayersEndpointsStyleVersionTests` (3 ケース: styleVersion フィールド存在 / PUT で +1 / asOf で過去 styleVersion) | 0.4d |
| **E'302** | `TilesEndpointsCacheControlTests` (3 ケース: `?sv=` で max-age=86400, `?asOf=` で no-store, 両方付きで no-store 優先) + `FakeGeoServerProxy.cs` 新規 (WireMock.NET or 簡易 stub) | 0.6d |

検証:
- `dotnet test api.tests -c Release` 全 green (E'1 解決後の 83 + 6 = 89 件 pass)
- full run と個別 run で結果一致

並列度: E'301 / E'302 は完全独立。WE'2 と並列可。

⚠️ **前提**: WE'1 (E'101) の DbReset 改善が完了していないとテスト追加で並列耐性問題が再発する。WE'1 マージ後着手推奨。

## WE'4 — WinForms SSE + batch UI + Docs

ブランチ: `feature/phase-e-prime-we4-winforms-sse-batch-docs`

| Issue | 内容 | 工数 |
|-------|------|------|
| **E'401** | `windos-app/Services/LayerEventListener.cs` 新規 (.NET 8 SSE + IObservable + 自動 reconnect 指数バックオフ) | 0.5d |
| **E'402** | MainForm 統合: layer 選択時 subscribe / close 時 unsubscribe / 受信時に bridge `tile_invalidate` envelope 発火 | 0.3d |
| **E'403** | `BatchAttributeEditDialog.cs` + `Designer.cs` 新規 + DataGridView 複数選択 → batch 編集ダイアログ起動 + `BatchUpdateFeaturesAsync` 呼び出し + 409 mismatch ハンドリング | 0.5d |
| **E'404** | `LayerEventListenerTests` + `BatchAttributeEditDialogTests` (新 5-7 件) | 0.2d |

検証:
- `dotnet test windos-app.tests` 全 green (123 + 7 = 130 件 pass)
- 手動: WinForms 属性編集 → 1 秒以内に WinForms 内 layer もタイル再ロード (Phase D' WebGIS の挙動と一致)
- 手動: 10 件選択 → 「一括属性編集」 → 1 リクエストで完了 (DevTools Network)

並列度: E'401 → E'402、E'403 は独立、E'404 は E'401-E'403 完了後。

## WE'5 (任意) — 完了サマリ + メモリ更新

ブランチ: なし (WE'4 PR に同梱)

| Issue | 内容 | 工数 |
|-------|------|------|
| **E'500** | `docs/PHASE_E_PRIME_COMPLETE.md` 作成 + `orchestration_state.md` 更新 + README 補正 | 0.1d |

WE'4 PR に同梱、独立 PR にしない (Phase D' の流儀と同じ)。

## 全 PR

| Wave | ブランチ | 想定 PR タイトル | base |
|------|---------|----------------|------|
| WE'0 | `feature/phase-e-prime-we0-design` | Phase E' WE'0: Plan + Design 3 本 + GetFeatureInfo ノート | main |
| WE'1 | `feature/phase-e-prime-we1-db-and-test-isolation` | Phase E' WE'1: DB クロージング (deleted_at DROP) + DbReset 並列耐性 | main |
| WE'2 | `feature/phase-e-prime-we2-winforms-asof` | Phase E' WE'2: WinForms asOf 配線 + AsOfState | main |
| WE'3 | `feature/phase-e-prime-we3-api-tests` | Phase E' WE'3: API テスト追加 (styleVersion + Cache-Control) | main |
| WE'4 | `feature/phase-e-prime-we4-winforms-sse-batch-docs` | Phase E' WE'4: WinForms SSE + batch UI + Docs | main |

すべて `base=main` (`stacked_pr_pitfall` 参照)。マージ順 WE'0 → WE'1 → (WE'2, WE'3) → WE'4 推奨。

## リスク

- **R1**: `deleted_at` DROP の API 影響範囲が見積もりより広い (recursive grep で `DeletedAt` プロパティが想定外の場所で使われている可能性) → WE'1 着手前に grep で総当たり確認
- **R2**: DbReset 並列耐性の原因が `xunit.runner.json` 設定ではなく、Postgres ロック競合 / connection pool 枯渇等の場合、修正コスト増 → WE'0 で 1 時間 PoC して原因確定
- **R3**: `LayerEventListener` の SSE 自動 reconnect 単体テストが flaky → `ITimeProvider` 抽象 + `FakeTimeProvider` (.NET 8) で時刻制御
- **R4**: `MainFormAsOfPickerTests` で WinForms UI thread (STA) 制約 → `AsOfState` ロジック切り出し方針で回避済
- **R5**: WE'1 の `FeatureEndpointsDeletedAtRegressionTests` 書き換えで Phase E の意図 (回帰検証) が薄れる → `fn_layer_delete` 呼び出しベースに変更し、回帰意図は維持
