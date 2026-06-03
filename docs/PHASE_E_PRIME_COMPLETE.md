# Phase E' 完了サマリ

Phase E' (Phase E クロージング + asOf 全面伝搬 + SSE WinForms 統合) 完了時点の高位サマリ。

## マージ済 PR (全 5 件)

| Wave | PR | 内容 |
|------|----|------|
| WE'0 | [#228](https://github.com/KenjiMiyakoshi/agri-gis/pull/228) | Plan + Design 3 本 + GetFeatureInfo ノート (E'' 送り) |
| WE'1 | [#229](https://github.com/KenjiMiyakoshi/agri-gis/pull/229) | E'101 DbReset 並列耐性 + E'102 0E08_drop_layers_deleted_at.sql + E'103 関数 v3 + E'104 SQL 13 箇所置換 + DTO 連鎖修正 |
| WE'2 | [#230](https://github.com/KenjiMiyakoshi/agri-gis/pull/230) | E'201 IApiClient asOf 引数 + E'202 ApiClient AppendAsOf + E'203 AsOfState 切り出し + E'204 AsOfStateTests + E'205 FakeApiClient 連鎖 |
| WE'3 | [#231](https://github.com/KenjiMiyakoshi/agri-gis/pull/231) | E'301 LayersEndpointsStyleVersionTests + E'302 TilesEndpointsCacheControlTests + FakeGeoServerHandler |
| WE'4 | 本 PR | E'401 LayerEventListener + DI 登録 + E'404 LayerEventListenerTests + Docs |

## 受入条件

1. ✅ `\d layers` で `deleted_at` 列が存在しない
2. ✅ `dotnet test api.tests -c Release` 全 green (Phase E 83 → 89 件)
3. ✅ `dotnet test windos-app.tests -c Release` 全 green (118 → 125 件、AsOfStateTests 5 + LayerEventListenerTests 2)
4. ✅ `dotnet test webgis (vitest)` 全 green (Phase D' 21 件 keep)
5. ✅ Phase D' WD'4 で発生した DbReset 並列耐性問題 (58 件失敗) が完全解消
6. ✅ 全 5 Wave が main にマージ済
7. ✅ `orchestration_state.md` メモリ更新

## 主要な実装メモ

- **layers.deleted_at DROP**: 1 migration + 3 関数 v3 + 13 SQL 箇所置換、Phase A 流儀「valid_to で表現する」イディオムを Phase E と完全統一
- **WinForms asOf 配線**: `IApiClient` 4 メソッドに `DateOnly? asOf` 引数追加、`AppendAsOf` 共通 helper で URL 構築一元化
- **AsOfState 切り出し**: `MainForm.cs` から asOf ロジックを `AsOfState` クラスに分離 (H5 リファクタの足場)
- **DbReset 並列耐性**: `layer_style_version` / `layer_history` の TRUNCATE 追加で `LayersEndpointsStyleVersionTests` が安定して通る
- **TilesEndpointsCacheControlTests**: `FakeGeoServerHandler` で WMS GetMap を 1x1 透過 PNG で stub、Cache-Control 文字列を直接検証
- **WinForms LayerEventListener**: 手書き SSE パーサ + 自動 reconnect (指数バックオフ 1s→30s 上限)、`?access_token=` 認証
- **BatchAttributeEditDialog (UI)**: 簡略 UI 実装の規模を抑えるため **Phase E'' 送り**、`BatchUpdateFeaturesAsync` ApiClient メソッドは WE'2 で完成済

## Phase E'' 申し送り

- **BatchAttributeEditDialog (WinForms UI)**: WE'4 で API クライアントは完成、UI は未実装
- **MainForm SSE 統合フル UI**: WE'4 で LayerEventListener と DI 登録は完成、MainForm からの subscribe + bridge envelope はオプション (Phase E'' or H5)
- **WMS GetFeatureInfo 本実装** (WE'0 で Design ノート作成)
- **Monaco エディタ統合** (D' WD'2 textarea からのアップグレード)
- **layer_history パーティショニング** (実測値到達後)
- **ライブプレビュー自動 PUT debounce** (D' WD'2 で明示保存のみ)
- **SldXmlBuilder TextSymbolizer / RasterSymbolizer**

## Phase H 送り

- 本番 GeoServer setup.ps1 自動化
- k8s helm chart
- MapProxy 中間キャッシュ
- SSE Redis pub-sub (複数 API インスタンス対応)

## 関連

- `PHASE_E_PRIME_INDEX.md` (着手時計画)
- `docs/layers-deleted-at-drop.md`, `test-isolation.md`, `winforms-event-listener.md`, `wms-getfeatureinfo-eprime2-note.md`
