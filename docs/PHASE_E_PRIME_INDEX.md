# Phase E' Index — Phase E クロージング + WinForms asOf/SSE + テスト基盤強化

agri-gis Phase E' (`Phase E クロージング + asOf 全面伝搬 + SSE WinForms 統合`) サイクルの高位サマリ。Phase E (バイテンポラル全面化) + Phase D' (テーマ編集 UI + SSE) 完了後の次サイクル。

## スコープ

Phase E では「`layers.deleted_at` を残したまま二重書きで動かす」「WebGIS だけ asOf 配線」など、意図的に残した残債がある。Phase D' では「WinForms 側 SSE 統合」と「ApiClient batch」をスコープ外にした。これらを E' で整理し、Phase A/B/C/D/E/D' の経路を完全に閉じる。

## 採用方針

| 観点 | 採用 |
|------|------|
| `layers.deleted_at` | **完全 DROP** (関数 v3 化 + WHERE 条件 `valid_to = '9999-12-31'` への置換) |
| WinForms asOf 配線 | `IApiClient` に `DateOnly? asOf` 引数追加 (4-5 メソッド)、MainForm が `_currentAsOf` を全 API に伝搬 |
| MainForm asOf テスト | `MainForm.cs` から `AsOfState` ロジック切り出し → unit test 化 (H5 への足場) |
| DbReset 並列耐性 | `layer_style_version` / `layer_history` の TRUNCATE 追加 + xunit Collection 戦略確認 |
| LayersEndpointsStyleVersion + TilesEndpointsCacheControl Tests | Phase D' 送り回収 |
| WinForms SSE | `LayerEventListener` クラス新設 (System.Net.ServerSentEvents) + MainForm 購読 |
| batch 編集モード UI | feature 一覧で複数選択 → 「一括属性編集」ダイアログ → `POST /api/features/batch` |
| WMS GetFeatureInfo | **Design ノートのみ E'**、本実装は E'' 送り |
| `layer_history` パーティショニング | E'' 送り (1000 万行級到達時) |
| 本番 GeoServer 自動化 / k8s helm | Phase H 送り |

## Wave 構成

| Wave | テーマ | 工数 | Issue |
|------|--------|------|------|
| **WE'0** | Plan + Design 3 本 + GetFeatureInfo ノート | 0.5d | E'100 |
| **WE'1** | DB クロージング (deleted_at DROP) + DbReset 並列耐性 | 1.5d | E'101-E'104 |
| **WE'2** | WinForms asOf 配線 + AsOfState 切り出し | 1.5d | E'201-E'204 |
| **WE'3** | API テスト追加 (styleVersion + Cache-Control) | 1.0d | E'301-E'302 |
| **WE'4** | WinForms SSE + batch UI + Docs | 1.5d | E'401-E'403 |
| | **合計** | **約 6.0d** | **14 Issue** |

クリティカルパス約 5 営業日 + バッファ。WE'2/WE'3 は並列可能。Phase D' (6.5d) とほぼ同規模。

詳細は `docs/issues/PHASE_E_PRIME_WAVE_PLAN.md`。

## 主要 API/DB 変更

### DB
- `0E08_drop_layers_deleted_at.sql` (up + down)
- `0E08b_fn_layer_delete_v3.sql` / `fn_layer_update_v2.sql` / `fn_layer_style_upsert_v2.sql` (deleted_at 経路削除)

### WinForms ApiClient (`IApiClient` 拡張)
- `GetLayersAsync(DateOnly? asOf, ct)`
- `GetLayerSchemaAsync(int layerId, DateOnly? asOf, ct)`
- `GetLayerStyleAsync(int layerId, DateOnly? asOf, ct)`
- `ListLayersAdminAsync(bool includeDeleted, DateOnly? asOf, ct)`
- `BatchUpdateFeaturesAsync(FeatureBatchUpdateRequest, ct)`

### WinForms 新規クラス
- `windos-app/Services/LayerEventListener.cs` (SSE 受信 + IObservable 配信 + 自動 reconnect 指数バックオフ)
- `windos-app/ViewModels/AsOfState.cs` (asOf 状態保持 + Read-only 判定)

## 受け入れ条件 (Phase E' 完了の定義)

1. `docker compose up -d` + migration 1 本 (`0E08`) + 関数差替 3 本適用成功
2. `\d layers` で `deleted_at` 列が存在しない
3. WinForms で過去時点モード ON → 全 API 呼び出しの URL に `?asOf=YYYY-MM-DD` 含まれる
4. WinForms 属性編集 → SSE 経由で **WinForms 自身も** layer 再ロード (Phase D' WD'3 で WebGIS は対応済、E' で WinForms 対応)
5. 複数 feature 選択 → 一括属性編集ダイアログ → 1 リクエストで N 件更新
6. `api.tests` 全 green (推定 85+ 件、styleVersion + Cache-Control テスト含む)
7. `windos-app.tests` 全 green (推定 125+ 件、`MainFormAsOfPickerTests` 5 件追加)
8. `webgis vitest` 全 green (Phase D' 21 件継続)
9. PR 単位で全 5 Wave が main にマージ済
10. `orchestration_state.md` メモリ更新

## Phase E'' 申し送り

- **WMS GetFeatureInfo 本実装** (E' Design ノートを起点)
- **Monaco エディタ統合** (D' WD'2 textarea からのアップグレード)
- **`layer_history` パーティショニング** (1000 万行級到達時)
- **ライブプレビュー自動 PUT debounce** (D' WD'2 で明示保存のみ)
- **SldXmlBuilder TextSymbolizer / RasterSymbolizer**
- **SSE Redis pub-sub 中継** (複数 API インスタンス時、Phase H と統合判断)

## 関連ドキュメント

- `PHASE_A_INDEX.md` 〜 `PHASE_E_INDEX.md`
- `PHASE_D_PRIME_INDEX.md` + `PHASE_D_PRIME_COMPLETE.md`
- `docs/issues/PHASE_E_PRIME_PLAN.md`
- `docs/issues/PHASE_E_PRIME_WAVE_PLAN.md`
- `docs/issues/PHASE_E_PRIME_ISSUES_INDEX.md`
- `docs/layers-deleted-at-drop.md`
- `docs/test-isolation.md`
- `docs/winforms-event-listener.md`
- `docs/wms-getfeatureinfo-eprime2-note.md` (E'' 送り Design ノート)

## 関連メモリ

- `orchestration_state.md` — 進捗
- `bitemporal_audit.md` — Phase A/E イディオム
- `stacked_pr_pitfall.md` — base=main 固定
- `smart_app_control_pitfall.md` — WinForms Release 構成
