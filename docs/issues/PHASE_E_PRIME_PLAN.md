# Phase E' Plan — 課題分析と採用案

Phase E' 着手前の課題分析と採用案。Phase D' `docs/issues/PHASE_D_PRIME_PLAN.md` 流儀踏襲。

## 0. 出発点

Phase E (バイテンポラル全面化) と Phase D' (テーマ編集 UI + SSE) で **意図的に残した残債** が 6 件。これらを E' で整理し、Phase A/B/C/D/E/D' の経路を完全に閉じる。

新規機能は導入しない (= クロージング + テスト強化 + UX 完成度向上)。Phase D' 末で見えた 1 件の品質問題 (testcontainer 並列耐性) も同サイクルで解決。

## 1. 課題 1: `layers.deleted_at` 列の DROP

### 1.1 現状

Phase E E105 で `fn_layer_delete v2` を導入したとき、後方互換のため `layers.deleted_at` を残し、`valid_to = CURRENT_DATE` と同時に `deleted_at = now()` も書く二重書きにした。

API 側の WHERE 条件は `AND l.deleted_at IS NULL` のままで、`valid_to` ベースの判定に切り替え終わっていない。

### 1.2 採用案

**案 A: deleted_at 完全 DROP + 全 WHERE 条件を `valid_to = '9999-12-31'::date` に置換**

- `0E08_drop_layers_deleted_at.sql` (up: DROP COLUMN、down: 列復元 + best effort backfill)
- 関数 v3 化: `fn_layer_delete`, `fn_layer_update`, `fn_layer_style_upsert` から `deleted_at` 操作削除
- endpoint 6 ファイル / 18 箇所の WHERE 条件置換 (AdminLayersEndpoints 6, LayerEndpoints 4, AdminLayerStyleEndpoints 2, FeatureEndpoints 6)
- DTO 拡張: `LayerAdminDto.DeletedAt` 列削除 (履歴は `layer_history.archived_at` で代替)
- WinForms `LayerAdminDto` 連鎖修正

落選:
- 案 B: `deleted_at` を keep + `valid_to` ベース判定の 2 段防御: 二重書きが残り、Phase A の「`valid_to` で表現する」イディオムが完全には適用されない
- 案 C: TRIGGER で `valid_to <> '9999-12-31'` 時に `deleted_at = now()` 自動付与: 後方互換は保てるが、コード追跡を複雑化、Phase A 流儀と相反

### 1.3 影響範囲

- DB: 1 migration + 3 関数差替
- API: 18 SQL 箇所 + DTO 1 件
- WinForms: `LayerAdminDto` + `LayerAdminForm` 表示 (削除日時カラム取り扱い)
- テスト: `FeatureEndpointsDeletedAtRegressionTests` 書き換え (関数呼び出しベースに)

## 2. 課題 2: WinForms ApiClient asOf 引数

### 2.1 現状

Phase E WE2 で API 側 6 endpoint に `?asOf=` 対応済。Phase E WE4 で WebGIS の `getLayers(asOf?)`, `getLayerExtent(asOf?)`, `getFeaturesAt(asOf?)` に asOf 引数追加済。

**WinForms `ApiClient` だけ asOf 未配線**。MainForm の asOfPicker で日付を設定しても、WinForms 内部の `IApiClient.GetLayersAsync(ct)` は asOf を送らない。WebGIS 経由のタイル表示は asOf 効くが、WinForms 側 layer 一覧は現在時点のまま。

### 2.2 採用案

**案 A: `IApiClient` の 4-5 メソッドに `DateOnly? asOf` 引数追加 + MainForm が `_currentAsOf` を全 API 呼び出しに伝搬**

```csharp
public interface IApiClient
{
    Task<IReadOnlyList<LayerDto>> GetLayersAsync(DateOnly? asOf, CancellationToken ct);
    Task<LayerSchemaResponseDto> GetLayerSchemaAsync(int layerId, DateOnly? asOf, CancellationToken ct);
    Task<JsonElement> GetLayerStyleAsync(int layerId, DateOnly? asOf, CancellationToken ct);
    Task<IReadOnlyList<LayerAdminDto>> ListLayersAdminAsync(
        bool includeDeleted, DateOnly? asOf, CancellationToken ct);
    Task<FeatureBatchUpdateResponse> BatchUpdateFeaturesAsync(
        FeatureBatchUpdateRequest req, CancellationToken ct);
    // ...
}
```

URL 構築は共通 helper:
```csharp
private static string AppendAsOf(string url, DateOnly? asOf)
    => asOf is null ? url : $"{url}{(url.Contains('?') ? '&' : '?')}asOf={asOf:yyyy-MM-dd}";
```

落選:
- 案 B: ApiClient 内部 state (`_currentAsOf`) で暗黙に asOf を送る: 透明性が低い、テストが書きにくい
- 案 C: HttpClient DelegatingHandler で URL を書き換える: HttpClient 全 fetch に影響、admin 系で意図しない asOf 伝搬

### 2.3 影響範囲

- WinForms: `IApiClient` + `ApiClient` (5 メソッド shift)、`MainForm` の API 呼び出し 8 箇所
- テスト: `FakeApiClient` + 既存 `windos-app.tests` の 38 件で `null` 渡しのコンパイル fix

## 3. 課題 3: MainFormAsOfPickerTests + AsOfState 切り出し

### 3.1 現状

`MainForm.cs` の OnAsOfEnabledChanged / OnAsOfPickerChanged ロジックがビューコントロールと混在。テスト未着手 (118 件 windos-app.tests のうち MainForm 系 0 件)。

将来 H5 (MainForm 分割) で本格分離するが、E' 段階では **asOf 周りのロジックだけ AsOfState クラスに切り出し** で先行整理する。

### 3.2 採用案

**案 A: `AsOfState` 値クラス + Read-only 判定 + イベント発火**

```csharp
public sealed class AsOfState
{
    public DateOnly? Current { get; private set; }
    public bool IsReadOnly => Current is not null;  // 過去時点モード時は編集 disable
    public event EventHandler<DateOnly?>? Changed;

    public void SetEnabled(bool enabled, DateOnly? defaultValue) { ... }
    public void SetValue(DateOnly value) { ... }
    public void Disable() { ... }
}
```

MainForm からは:
```csharp
private readonly AsOfState _asOf = new();
// ...
_asOf.Changed += (s, asOf) => {
    saveButton.Enabled = !_asOf.IsReadOnly;
    BridgeSendAsOfChange(asOf);
    _ = ReloadLayersAsync(asOf);  // ← E'2 で asOf 伝搬
};
```

落選:
- 案 B: MainForm にロジックを残したまま reflection でテスト: 非推奨
- 案 C: View-Model 全面導入 (MVVM): H5 リファクタの先取り、E' のスコープを超える

### 3.3 影響範囲

- `windos-app/ViewModels/AsOfState.cs` 新規
- `MainForm.cs` の OnAsOfEnabledChanged / OnAsOfPickerChanged を `_asOf` に委譲
- `windos-app.tests/ViewModels/AsOfStateTests.cs` 5 件

## 4. 課題 4: DbReset 並列耐性

### 4.1 現状

Phase D' WD'4 で `LayersEndpointsStyleVersionTests` を追加したところ、`api.tests` の full run で 58 件失敗が発生。個別 run では pass。`DbReset.cs:29` で例外、testcontainer のコンテナ pool が枯渇? あるいは並列実行で TRUNCATE 順序が衝突?

詳細未解明、Phase D'' 送りになっていた。

### 4.2 採用案 (調査込み)

**案 A: WE'0 でまず 1 時間の調査 PoC を行い、原因確定後に対応策を WE'1 で実装**

調査項目:
- `xunit.runner.json` の `parallelizeAssembly` / `parallelizeTestCollections` 設定確認
- `DbReset.RunAsync` の TRUNCATE 順 (`layer_style_version` / `layer_history` が CASCADE で巻き込まれるか)
- `PostgisContainerFixture` の lifetime (`[Collection]` で共有されてるか、テストごとに新規か)

対応候補:
- 候補 a: TRUNCATE に `layer_style_version` / `layer_history` を明示追加
- 候補 b: テストごとに dedicated schema (Postgres `CREATE SCHEMA test_xxx`) を切り、衝突回避
- 候補 c: 単純に test collection を更に細かく分割

落選:
- 全テスト直列化 (parallelism=1): テスト実行時間が 2-3 倍に膨らむ

### 4.3 影響範囲

- `api.tests/Fixtures/DbReset.cs` (TRUNCATE 拡張 or schema 戦略)
- `xunit.runner.json` 設定
- Phase D' WD'4 で送りにした 2 テスト追加が安全に行えるようになる

## 5. 課題 5: API テスト追加

### 5.1 採用案

Phase D' WD'4 で送りにした 2 テスト。**WE'1 (4) 完了が前提**。

- **LayersEndpointsStyleVersionTests** (2-3 ケース): GET /api/layers の `styleVersion` フィールド + PUT 後の +1 検証 + asOf 時の過去 styleVersion 取得
- **TilesEndpointsCacheControlTests** (2-3 ケース): `?sv=N` で `max-age=86400`, `?asOf=` で `no-store`, 競合時 `no-store` 優先

Tile 系は GeoServer Fake が必要 (`FakeGeoServerProxy` for testcontainer)。

### 5.2 影響範囲

- `api.tests/Tests/Layers/LayersEndpointsStyleVersionTests.cs` 新規
- `api.tests/Tests/Tiles/TilesEndpointsCacheControlTests.cs` 新規
- `api.tests/Fixtures/FakeGeoServerProxy.cs` 新規 (WireMock.NET or 簡易 stub)

## 6. 課題 6: WinForms SSE + batch 編集モード UI

### 6.1 現状

Phase D' WD'3 で WebGIS の eventStream.ts は実装済。WinForms 側の SSE 統合 (`LayerEventListener`) と、`POST /api/features/batch` を呼ぶ UI は Phase D'' 送りになっていた。E' で完成させる。

### 6.2 採用案

**案 A: `LayerEventListener` クラス + MainForm 統合 + 「一括属性編集」ダイアログ**

- `LayerEventListener`:
  - .NET 8 `System.Net.ServerSentEvents` (or HttpClient + StreamReader 手書き) で SSE 受信
  - `IObservable<LayerInvalidationEvent>` 公開
  - 認証: `?access_token=` クエリ (Phase D' EventsEndpoints 仕様)
  - 自動 reconnect: 指数バックオフ (1s / 2s / 4s / 8s、上限 30s)
- `MainForm`:
  - レイヤ選択時に `_eventListener.Subscribe(layerId)`
  - 受信時に bridge `tile_invalidate` envelope 発火 + 該当 feature 編集ダイアログが開いていれば再 GET
- batch UI:
  - feature 一覧表 (DataGridView) に複数選択 + 「一括属性編集」ボタン
  - ダイアログで属性 patch 入力 → `IApiClient.BatchUpdateFeaturesAsync` 呼び出し
  - 楽観ロック失敗 (409) 時に mismatched IDs を表示

落選:
- 案 B: WebSocket 化: SSE で十分 (HTTP/1.1 単方向、サーバ→クライアントの片方向通知のみ)
- 案 C: WebGIS bridge 経由で WinForms に通知: bridge は WinForms→WebGIS 主体、WebGIS→WinForms は事故の元

### 6.3 影響範囲

- `windos-app/Services/LayerEventListener.cs` 新規 (~200 行)
- `windos-app/Forms/BatchAttributeEditDialog.cs` + `Designer.cs` 新規
- `MainForm.cs` の layer 切替時 + close 時のサブスクライブ管理
- `IApiClient.BatchUpdateFeaturesAsync` (Phase D' D'104 で API 完成、client side のみ追加)

## 7. WMS GetFeatureInfo の取り扱い

Phase E' では **Design ノートのみ** 作成し、本実装は E'' へ送る:

- `docs/wms-getfeatureinfo-eprime2-note.md` 作成
- 内容: GetFeatureInfo の利点 (バイテンポラル CQL_FILTER で履歴属性も取れる) と現状 `/api/layers/{id}/at` との比較、E'' 着手時の判断軸
- Phase D' 送り課題のうち、E' で扱わない明示的根拠

## 8. 残課題 (E'' 候補)

- WMS GetFeatureInfo 本実装
- Monaco エディタ統合 (D' WD'2 textarea からのアップグレード)
- ライブプレビュー自動 PUT debounce
- SldXmlBuilder TextSymbolizer / RasterSymbolizer
- `layer_history` パーティショニング (実測値到達後)
- SSE Redis pub-sub (複数 API インスタンス、Phase H と統合判断)

## 関連

- `PHASE_E_PRIME_INDEX.md`
- `PHASE_E_PRIME_WAVE_PLAN.md`
- `PHASE_E_PRIME_ISSUES_INDEX.md`
