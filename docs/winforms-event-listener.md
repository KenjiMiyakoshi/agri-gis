# WinForms LayerEventListener (Phase E' E'6)

Phase D' D'301-D'303 で実装した SSE (Server-Sent Events) 経路を WinForms 側で購読し、他クライアント編集の自動反映 + batch 編集モード UI を実装する。

## 背景

Phase D' で:
- API 側 `EventsEndpoints` (`GET /api/events/layers/{layerId}/stream`) 実装済
- DB トリガー `0F02_notify_invalidation.sql` 実装済
- WebGIS 側 `eventStream.ts` で `EventSource` 購読 + 自動反映済

WinForms 側 `LayerEventListener` は Phase D' 送りで未実装。E' で完成させる。

## 採用方針

### LayerEventListener クラス

`windos-app/Services/LayerEventListener.cs` 新規:

```csharp
public sealed class LayerEventListener : IAsyncDisposable
{
    private readonly IApiClient _api;
    private readonly ISessionStore _session;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly Subject<LayerInvalidationEvent> _events = new();
    public IObservable<LayerInvalidationEvent> Events => _events;

    public void Subscribe(int layerId)
    {
        Unsubscribe();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(layerId, _cts.Token));
    }

    private async Task LoopAsync(int layerId, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConsumeStreamAsync(layerId, ct);
                delay = TimeSpan.FromSeconds(1);   // reset on success
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[LayerEventListener] {ex.Message}");
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }

    private async Task ConsumeStreamAsync(int layerId, CancellationToken ct)
    {
        var jwt = _session.Current?.AccessToken
            ?? throw new InvalidOperationException("no JWT");
        var url = $"/api/events/layers/{layerId}/stream?access_token={Uri.EscapeDataString(jwt)}";
        using var resp = await _api.HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        string? line;
        string? eventName = null;
        var dataSb = new StringBuilder();
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (line.StartsWith("event:"))
            {
                eventName = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:"))
            {
                dataSb.Append(line["data:".Length..].Trim());
            }
            else if (string.IsNullOrEmpty(line) && eventName == "layer_invalidate")
            {
                var ev = JsonSerializer.Deserialize<LayerInvalidationEvent>(dataSb.ToString());
                if (ev is not null) _events.OnNext(ev);
                eventName = null;
                dataSb.Clear();
            }
        }
    }

    public void Unsubscribe()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    public async ValueTask DisposeAsync()
    {
        Unsubscribe();
        _events.OnCompleted();
        _events.Dispose();
        if (_loop is not null) await _loop;
    }
}
```

### MainForm 統合

```csharp
private readonly LayerEventListener _eventListener;

protected override void OnLoad(EventArgs e)
{
    base.OnLoad(e);
    _eventListener.Events.Subscribe(OnLayerInvalidated);
}

private void OnLayerSelected(int layerId)
{
    _eventListener.Subscribe(layerId);
    // ...
}

private void OnLayerInvalidated(LayerInvalidationEvent ev)
{
    if (InvokeRequired) { Invoke(() => OnLayerInvalidated(ev)); return; }
    // bridge envelope で WebGIS 側にも通知
    _bridge.Send(new TileInvalidateEnvelope(ev.LayerId, ev.StyleVersion));
    // 開いている feature 編集ダイアログがあれば再 GET
    foreach (var dlg in OpenFeatureDialogs)
    {
        _ = dlg.RefreshAsync();
    }
}
```

### batch 編集モード UI

`BatchAttributeEditDialog.cs` + `Designer.cs` 新規:
- 入力: 選択された feature の entity_id リスト + 各 version (If-Match)
- UI: 属性 patch 入力フォーム (全件共通の値) + 楽観ロック失敗時の mismatch 表示
- 送信: `IApiClient.BatchUpdateFeaturesAsync(new FeatureBatchUpdateRequest { ... })`

MainForm の DataGridView (feature 一覧) に複数選択モード + 「一括属性編集」ボタン追加。

### Reconnect 戦略

EventSource は外部 reconnect しないので、自前で `try { ConsumeStreamAsync } catch { delay; } loop` を実装。指数バックオフ 1s → 2s → 4s → 8s → 16s → 30s (上限) で安定。

`ITimeProvider` 抽象化 + `FakeTimeProvider` でテスト時に時刻制御 (`LayerEventListenerTests` の flaky 回避)。

## 受入条件

1. WinForms 起動 + layer 選択 → `LayerEventListener` が `/api/events/.../stream` に接続
2. 別クライアント (psql で `INSERT INTO feature_current ...` 等) で変更 → SSE 受信 → WinForms ステータス表示更新
3. ネット断 (5 秒) → 自動 reconnect → 再接続後の変更受信
4. 10 件選択 → 「一括属性編集」 → 1 リクエストで 10 件更新
5. 1 件 version mismatch → 409 表示、他 9 件もすべて rollback (all-or-nothing)
6. `LayerEventListenerTests` 5 件 pass (reconnect バックオフ含む)

## 関連

- `PHASE_E_PRIME_INDEX.md`
- Phase D' Design `docs/feature-events-sse.md`
- Phase D' Design `docs/feature-batch-update.md`
- `docs/api-events.md` (SSE プロトコル仕様)
